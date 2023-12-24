/*
*   Copyright 2023 Robert Koifman
*   
*   Licensed under the Apache License, Version 2.0 (the "License");
*   you may not use this file except in compliance with the License.
*   You may obtain a copy of the License at
*
*   http://www.apache.org/licenses/LICENSE-2.0
*
*   Unless required by applicable law or agreed to in writing, software
*   distributed under the License is distributed on an "AS IS" BASIS,
*   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
*   See the License for the specific language governing permissions and
*   limitations under the License.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using System.Net;
using System.Net.Sockets;
using System.Threading;

using Softnet.ServerKit;

namespace Softnet.Proxy
{
    static class TcpDispatcher
    {
        static LinkedList<TCPv6OptionsRecord> TCPv6OptionsList;
        static LinkedList<TCPv4OptionsRecord> TCPv4OptionsList;
        static LinkedList<TcpControl> TCPControlList;
        static LinkedList<TcpConnectorV6> ConnectorV6List;
        static LinkedList<TcpConnectorV4> ConnectorV4List;

        static Dictionary<IEPv6Key, TcpProxyV6> ProxyV6List;
        static Dictionary<long, TcpProxyV4> ProxyV4List;

        public static VirtualTCPOptions[] VrtTCPOptionsTable;

        public static void Init()
        {
            TCPv6OptionsList = new LinkedList<TCPv6OptionsRecord>();
            TCPv4OptionsList = new LinkedList<TCPv4OptionsRecord>();
            TCPControlList = new LinkedList<TcpControl>();
            ConnectorV6List = new LinkedList<TcpConnectorV6>();
            ConnectorV4List = new LinkedList<TcpConnectorV4>();

            ProxyV6List = new Dictionary<IEPv6Key, TcpProxyV6>(new IEPv6KeyComparer());
            ProxyV4List = new Dictionary<long, TcpProxyV4>();

            ScheduledTask task = new ScheduledTask(RemoveExpiredItems, null);
            Softnet.ServerKit.TaskScheduler.Add(task, 60);

            BuildTCPOptionsTable();
        }

        public static void Clear()
        {
            lock (ConnectorV6List)
            {
                foreach (TcpConnectorV6 connector in ConnectorV6List)
                    connector.Close();
            }

            lock (ConnectorV4List)
            {
                foreach (TcpConnectorV4 connector in ConnectorV4List)
                    connector.Close();
            }
        }

        static void RemoveExpiredItems(object noData)
        {
            long currentTime = SystemClock.Seconds;

            lock (TCPv6OptionsList)
            {
                while (TCPv6OptionsList.Count > 0)
                {
                    if (TCPv6OptionsList.Last.Value.deathTime <= currentTime)
                        TCPv6OptionsList.RemoveLast();
                    else
                        break;
                }
            }

            lock (TCPv4OptionsList)
            {
                while (TCPv4OptionsList.Count > 0)
                {
                    if (TCPv4OptionsList.Last.Value.deathTime <= currentTime)
                        TCPv4OptionsList.RemoveLast();
                    else
                        break;
                }
            }

            lock (TCPControlList)
            {
                while (TCPControlList.Count > 0)
                {
                    if (TCPControlList.Last.Value.deathTime <= currentTime)
                        TCPControlList.RemoveLast();
                    else
                        break;
                }
            }

            lock (ConnectorV6List)
            {
                while (ConnectorV6List.Count > 0)
                {
                    if (ConnectorV6List.Last.Value.deathTime <= currentTime)
                        ConnectorV6List.RemoveLast();
                    else
                        break;
                }            
            }

            lock (ConnectorV4List)
            {
                while (ConnectorV4List.Count > 0)
                {
                    if (ConnectorV4List.Last.Value.deathTime <= currentTime)
                        ConnectorV4List.RemoveLast();
                    else
                        break;
                }
            }

            ScheduledTask task = new ScheduledTask(RemoveExpiredItems, null);
            Softnet.ServerKit.TaskScheduler.Add(task, 10);
        }

        public static TcpControl GetControl(Guid connectionUid)
        {
            lock (TCPControlList)
            {
                if (TCPControlList.Count > 0)
                {
                    LinkedListNode<TcpControl> node = TCPControlList.First;
                    while (node != null)
                    {
                        if (node.Value.connectionUid.Equals(connectionUid))
                            return node.Value;
                        node = node.Next;
                    }
                }

                TcpControl control = new TcpControl(connectionUid);
                TCPControlList.AddFirst(control);

                return control;
            }
        }

        public static TcpControl FindControl(Guid connectionUid)
        {
            lock (TCPControlList)
            {
                if (TCPControlList.Count > 0)
                {
                    LinkedListNode<TcpControl> node = TCPControlList.First;
                    while (node != null)
                    {
                        if (node.Value.connectionUid.Equals(connectionUid))
                            return node.Value;
                        node = node.Next;
                    }
                }
                return null;
            }
        }

        #region ---------- Handling IPv6 packets --------------------------------------

        public static void RegisterConnector(TcpConnectorV6 connector)
        {
            lock (ConnectorV6List)
            {
                ConnectorV6List.AddFirst(connector);
            }
        }

        public static void StoreTCPv6Options(SocketAsyncEventArgs saea)
        {
            IPAddress remoteIp = ((IPEndPoint)saea.RemoteEndPoint).Address;

            byte[] iepBytes = new byte[18];
            Buffer.BlockCopy(remoteIp.GetAddressBytes(), 0, iepBytes, 0, 16);
            Buffer.BlockCopy(saea.Buffer, saea.Offset, iepBytes, 16, 2);

            IEPv6Key endpointKey = new IEPv6Key(iepBytes);
            TCPOptions tcpOptions = ReadTCPOptions(saea, saea.Offset);
            TCPv6OptionsRecord record = new TCPv6OptionsRecord(endpointKey, tcpOptions);

            lock (TCPv6OptionsList)
            {
                TCPv6OptionsList.AddFirst(record);
                while (TCPv6OptionsList.Count >= Constants.max_tcp_params_items)
                    TCPv6OptionsList.RemoveLast();
            }            
        }

        public static TCPOptions FindTCPv6Options(IEPv6Key endpointKey)
        {
            lock (TCPv6OptionsList)
            {
                if (TCPv6OptionsList.Count > 0)
                {
                    LinkedListNode<TCPv6OptionsRecord> node = TCPv6OptionsList.First;
                    while (node != null)
                    {
                        if (node.Value.key.hashCode == endpointKey.hashCode && node.Value.key.Equals(endpointKey))
                            return node.Value.tcpOptions;
                        node = node.Next;
                    }
                }
            }
            return null;
        }

        public static void HandleSegmentV6(SocketAsyncEventArgs saea, int serverPort)
        {
            IPAddress remoteIp = ((IPEndPoint)saea.RemoteEndPoint).Address;
            byte[] iepBytes = new byte[20];
            Buffer.BlockCopy(remoteIp.GetAddressBytes(), 0, iepBytes, 0, 16);
            Buffer.BlockCopy(saea.Buffer, saea.Offset, iepBytes, 16, 2);
            Buffer.BlockCopy(saea.Buffer, saea.Offset + 2, iepBytes, 18, 2);

            IEPv6Key endpointKey = new IEPv6Key(iepBytes);

            TcpProxyV6 tcpProxy = null;
            lock (ProxyV4List)
            {
                ProxyV6List.TryGetValue(endpointKey, out tcpProxy);
            }

            if (tcpProxy != null)
            {
                int FLAG_BITS = saea.Buffer[saea.Offset + 13];
                if ((FLAG_BITS & SYN) == 0)
                {
                    tcpProxy.HandleSegment(saea);
                }
                else
                {
                    if (SystemClock.Seconds < tcpProxy.LastSegmentReceivedSeconds + Constants.TcpProxySessionRenewSeconds)
                    {
                        tcpProxy.HandleSegment(saea);
                    }
                    else
                    {
                        tcpProxy.Remove();

                        var peerProxy = tcpProxy.PeerProxy;
                        if (peerProxy != null)
                        {
                            peerProxy.Remove();
                        }

                        var newProxy = new TcpProxyV6(endpointKey);
                        newProxy.Init(serverPort);

                        lock (ProxyV6List)
                        {
                            ProxyV6List.Add(endpointKey, newProxy);
                        }

                        Softnet.ServerKit.Monitor.Add(newProxy);

                        int index = serverPort - Constants.BasicProxyPort;
                        newProxy.VrtTCPOptions = VrtTCPOptionsTable[index];
                        newProxy.HostTCPOptions = ReadTCPOptions(saea, saea.Offset);

                        newProxy.HandleSegment(saea);
                    }
                }
            }
            else
            {
                int FLAG_BITS = saea.Buffer[saea.Offset + 13];
                if ((FLAG_BITS & SYN) != 0)
                {
                    try
                    {
                        var newProxy = new TcpProxyV6(endpointKey);
                        newProxy.Init(serverPort);

                        lock (ProxyV6List)
                        {
                            if (ProxyV6List.Count > Constants.max_tcp_v6_proxy_items)
                                throw new ArgumentException();

                            ProxyV6List.Add(endpointKey, newProxy);
                        }

                        Softnet.ServerKit.Monitor.Add(newProxy);

                        int index = serverPort - Constants.BasicProxyPort;
                        newProxy.VrtTCPOptions = VrtTCPOptionsTable[index];
                        newProxy.HostTCPOptions = ReadTCPOptions(saea, saea.Offset);

                        newProxy.HandleSegment(saea);
                    }
                    catch (ArgumentException)
                    {
                        TCPSaeaPool.Add(saea);
                    }
                }
                else
                {
                    BuildRstHeaderV6(saea, serverPort);
                    RawTcpBindingV4.Send(saea);
                }
            }
        }

        public static void RemoveProxyV6(IEPv6Key endpointKey)
        {
            lock (ProxyV6List)
            {
                ProxyV6List.Remove(endpointKey);
            }        
        }

        #endregion

        #region ---------- Handling IPv4 packets --------------------------------------

        public static void RegisterConnector(TcpConnectorV4 connector)
        {
            lock (ConnectorV4List)
            {
                ConnectorV4List.AddFirst(connector);
            }
        }

        public static void StoreTCPv4Options(SocketAsyncEventArgs saea, int tcpOffset)
        {
            byte[] keyBytes = new byte[] { 0, 0, 0, 0, 0, 0, 0, 0 };
            Buffer.BlockCopy(saea.Buffer, saea.Offset + 12, keyBytes, 2, 4);
            Buffer.BlockCopy(saea.Buffer, tcpOffset, keyBytes, 6, 2);

            long endpointKey = ByteConverter.ToInt64(keyBytes, 0);
            TCPOptions tcpOptions = ReadTCPOptions(saea, tcpOffset);
            TCPv4OptionsRecord record = new TCPv4OptionsRecord(endpointKey, tcpOptions);

            lock (TCPv4OptionsList)
            {
                TCPv4OptionsList.AddFirst(record);
                while (TCPv4OptionsList.Count >= Constants.max_tcp_params_items)
                    TCPv4OptionsList.RemoveLast();
            }  
        }

        public static TCPOptions FindTCPv4Options(long endpointKey)
        {
            lock (TCPv4OptionsList)
            {
                if (TCPv4OptionsList.Count > 0)
                {
                    LinkedListNode<TCPv4OptionsRecord> node = TCPv4OptionsList.First;
                    while (node != null)
                    {
                        if (node.Value.key == endpointKey)
                            return node.Value.tcpOptions;
                        node = node.Next;
                    }
                }
            }
            return null;
        }

        public static void HandleSegmentV4(SocketAsyncEventArgs saea, int tcpOffset, int tcpSize, int serverPort)
        {
            byte[] packet = saea.Buffer;
            int offset = saea.Offset;

            byte[] keyBytes = new byte[8];
            Buffer.BlockCopy(packet, offset + 12, keyBytes, 0, 4);
            Buffer.BlockCopy(packet, tcpOffset, keyBytes, 4, 2);
            Buffer.BlockCopy(packet, tcpOffset + 2, keyBytes, 6, 2);

            long endpointKey = ByteConverter.ToInt64(keyBytes, 0);

            TcpProxyV4 tcpProxy = null;
            lock (ProxyV4List)
            {
                ProxyV4List.TryGetValue(endpointKey, out tcpProxy);
            }

            if (tcpProxy != null)
            {
                int FLAG_BITS = saea.Buffer[tcpOffset + 13];
                if ((FLAG_BITS & SYN) == 0)
                {
                    tcpProxy.HandleSegment(saea, tcpOffset, tcpSize);
                }
                else
                {
                    if (SystemClock.Seconds < tcpProxy.LastSegmentReceivedSeconds + Constants.TcpProxySessionRenewSeconds)
                    {
                        tcpProxy.HandleSegment(saea, tcpOffset, tcpSize);
                    }
                    else
                    {
                        tcpProxy.Remove();

                        var peerProxy = tcpProxy.PeerProxy;
                        if (peerProxy != null)
                        {
                            peerProxy.Remove();
                        }

                        var newProxy = new TcpProxyV4(endpointKey);
                        newProxy.Init(serverPort);

                        lock (ProxyV4List)
                        {
                            ProxyV4List.Add(endpointKey, newProxy);
                        }

                        Softnet.ServerKit.Monitor.Add(newProxy);

                        int index = serverPort - Constants.BasicProxyPort;
                        newProxy.VrtTCPOptions = VrtTCPOptionsTable[index];
                        newProxy.HostTCPOptions = ReadTCPOptions(saea, tcpOffset);

                        newProxy.HandleSegment(saea, tcpOffset, tcpSize);
                    }
                }
            }
            else
            {
                int FLAG_BITS = saea.Buffer[tcpOffset + 13];
                if ((FLAG_BITS & SYN) != 0)
                {
                    try
                    {
                        var newProxy = new TcpProxyV4(endpointKey);
                        newProxy.Init(serverPort);

                        lock (ProxyV4List)
                        {
                            if (ProxyV4List.Count > Constants.max_tcp_v4_proxy_items)
                                throw new ArgumentException();

                            ProxyV4List.Add(endpointKey, newProxy);
                        }

                        Softnet.ServerKit.Monitor.Add(newProxy);

                        int index = serverPort - Constants.BasicProxyPort;
                        newProxy.VrtTCPOptions = VrtTCPOptionsTable[index];
                        newProxy.HostTCPOptions = ReadTCPOptions(saea, tcpOffset);

                        newProxy.HandleSegment(saea, tcpOffset, tcpSize);
                    }
                    catch (ArgumentException)
                    {
                        TCPSaeaPool.Add(saea);
                    }
                }
                else
                {
                    BuildRstHeaderV4(saea, tcpOffset, serverPort);
                    RawTcpBindingV4.Send(saea);
                }            
            }
        }

        public static void RemoveProxyV4(long endpointKey)
        {
            lock (ProxyV4List)
            {
                ProxyV4List.Remove(endpointKey);
            }
        }

        #endregion

        static TCPOptions ReadTCPOptions(SocketAsyncEventArgs saea, int tcpOffset)
        {
            TCPOptions tcpOptions = new TCPOptions();
            byte[] packet = saea.Buffer;

            int header_size = (packet[tcpOffset + 12] >> 4) * 4;
            int end_option_offset = tcpOffset + header_size;
            int next_option_offset = tcpOffset + 20;

            while (next_option_offset < end_option_offset)
            {
                int Kind = packet[next_option_offset];
                if (Kind == 0 || Kind == 1)
                {
                    next_option_offset++;
                    continue;
                }

                int Length = packet[next_option_offset + 1];

                if (Kind == 2)
                {
                    if (Length != 4)
                        break;
                    tcpOptions.MSS = packet[next_option_offset + 2] * 256 + packet[next_option_offset + 3];
                }
                else if (Kind == 3)
                {
                    if (Length != 3)
                        break;
                    tcpOptions.WindowScaleSupported = true;
                    tcpOptions.WindowScale = packet[next_option_offset + 2];
                }
                else if (Kind == 4)
                {
                    if (Length != 2)
                        break;
                    tcpOptions.SACKPermitted = true;
                }
                else if (Length == 0)
                {
                    break;
                }

                next_option_offset += Length;
            }

            return tcpOptions;
        }

        static void BuildRstHeaderV4(SocketAsyncEventArgs saea, int tcpOffset, int serverPort)
        { 
        
        }

        static void BuildRstHeaderV6(SocketAsyncEventArgs saea, int serverPort)
        { 
        
        }

        //--------------------------------------------------------------------------------------------------------------------------------

        const byte TCP_DATA_OFFSET_20 = 0x50;
        const byte RST_BYTE = 0x04;

        const int SYN = 0x02;

        class TCPv6OptionsRecord
        {
            public readonly IEPv6Key key;
            public readonly long deathTime;
            public readonly TCPOptions tcpOptions;

            public TCPv6OptionsRecord(IEPv6Key endpointKey, TCPOptions tcpOptions)
            {
                this.key = endpointKey;
                this.deathTime = SystemClock.Seconds + 40;
                this.tcpOptions = tcpOptions;
            }
        }

        class TCPv4OptionsRecord
        {
            public readonly long key;
            public readonly long deathTime;
            public readonly TCPOptions tcpOptions;

            public TCPv4OptionsRecord(long endpointKey, TCPOptions tcpOptions)
            {
                this.key = endpointKey;
                this.deathTime = SystemClock.Seconds + 40;
                this.tcpOptions = tcpOptions;
            }
        }

        const int BasicProxyPort = Constants.BasicProxyPort;

        static void BuildTCPOptionsTable()
        {
            VrtTCPOptionsTable = new VirtualTCPOptions[] 
            {
                // Window scale supported. SACK permitted.

                new VirtualTCPOptions(BasicProxyPort, 1460, true, 0, true),       //  37780
                new VirtualTCPOptions(BasicProxyPort + 1, 1460, true, 1, true),
                new VirtualTCPOptions(BasicProxyPort + 2, 1460, true, 2, true),
                new VirtualTCPOptions(BasicProxyPort + 3, 1460, true, 3, true),
                new VirtualTCPOptions(BasicProxyPort + 4, 1460, true, 4, true),
                new VirtualTCPOptions(BasicProxyPort + 5, 1460, true, 5, true),
                new VirtualTCPOptions(BasicProxyPort + 6, 1460, true, 6, true),
                new VirtualTCPOptions(BasicProxyPort + 7, 1460, true, 7, true),
                new VirtualTCPOptions(BasicProxyPort + 8, 1460, true, 8, true), 
                new VirtualTCPOptions(BasicProxyPort + 9, 1460, true, 9, true),
                new VirtualTCPOptions(BasicProxyPort + 10, 1460, true, 10, true),
                new VirtualTCPOptions(BasicProxyPort + 11, 1460, true, 11, true),
                new VirtualTCPOptions(BasicProxyPort + 12, 1460, true, 12, true),
                new VirtualTCPOptions(BasicProxyPort + 13, 1460, true, 13, true),
                new VirtualTCPOptions(BasicProxyPort + 14, 1460, true, 14, true), //  37794

                new VirtualTCPOptions(BasicProxyPort + 15, 1456, true, 0, true),  //  37795
                new VirtualTCPOptions(BasicProxyPort + 16, 1456, true, 1, true),
                new VirtualTCPOptions(BasicProxyPort + 17, 1456, true, 2, true),
                new VirtualTCPOptions(BasicProxyPort + 18, 1456, true, 3, true),
                new VirtualTCPOptions(BasicProxyPort + 19, 1456, true, 4, true),
                new VirtualTCPOptions(BasicProxyPort + 20, 1456, true, 5, true),
                new VirtualTCPOptions(BasicProxyPort + 21, 1456, true, 6, true),
                new VirtualTCPOptions(BasicProxyPort + 22, 1456, true, 7, true),
                new VirtualTCPOptions(BasicProxyPort + 23, 1456, true, 8, true), 
                new VirtualTCPOptions(BasicProxyPort + 24, 1456, true, 9, true),
                new VirtualTCPOptions(BasicProxyPort + 25, 1456, true, 10, true),
                new VirtualTCPOptions(BasicProxyPort + 26, 1456, true, 11, true),
                new VirtualTCPOptions(BasicProxyPort + 27, 1456, true, 12, true),
                new VirtualTCPOptions(BasicProxyPort + 28, 1456, true, 13, true),
                new VirtualTCPOptions(BasicProxyPort + 29, 1456, true, 14, true),  //  37809

                new VirtualTCPOptions(BasicProxyPort + 30, 1452, true, 0, true),   //  37810
                new VirtualTCPOptions(BasicProxyPort + 31, 1452, true, 1, true),
                new VirtualTCPOptions(BasicProxyPort + 32, 1452, true, 2, true),
                new VirtualTCPOptions(BasicProxyPort + 33, 1452, true, 3, true),
                new VirtualTCPOptions(BasicProxyPort + 34, 1452, true, 4, true),
                new VirtualTCPOptions(BasicProxyPort + 35, 1452, true, 5, true),
                new VirtualTCPOptions(BasicProxyPort + 36, 1452, true, 6, true),
                new VirtualTCPOptions(BasicProxyPort + 37, 1452, true, 7, true),
                new VirtualTCPOptions(BasicProxyPort + 38, 1452, true, 8, true),
                new VirtualTCPOptions(BasicProxyPort + 39, 1452, true, 9, true),
                new VirtualTCPOptions(BasicProxyPort + 40, 1452, true, 10, true),
                new VirtualTCPOptions(BasicProxyPort + 41, 1452, true, 11, true),
                new VirtualTCPOptions(BasicProxyPort + 42, 1452, true, 12, true),
                new VirtualTCPOptions(BasicProxyPort + 43, 1452, true, 13, true),
                new VirtualTCPOptions(BasicProxyPort + 44, 1452, true, 14, true),   //  37824

                new VirtualTCPOptions(BasicProxyPort + 45, 1448, true, 0, true),   //  37825
                new VirtualTCPOptions(BasicProxyPort + 46, 1448, true, 1, true),
                new VirtualTCPOptions(BasicProxyPort + 47, 1448, true, 2, true),
                new VirtualTCPOptions(BasicProxyPort + 48, 1448, true, 3, true),
                new VirtualTCPOptions(BasicProxyPort + 49, 1448, true, 4, true),
                new VirtualTCPOptions(BasicProxyPort + 50, 1448, true, 5, true),
                new VirtualTCPOptions(BasicProxyPort + 51, 1448, true, 6, true),
                new VirtualTCPOptions(BasicProxyPort + 52, 1448, true, 7, true),
                new VirtualTCPOptions(BasicProxyPort + 53, 1448, true, 8, true),
                new VirtualTCPOptions(BasicProxyPort + 54, 1448, true, 9, true),
                new VirtualTCPOptions(BasicProxyPort + 55, 1448, true, 10, true),
                new VirtualTCPOptions(BasicProxyPort + 56, 1448, true, 11, true),
                new VirtualTCPOptions(BasicProxyPort + 57, 1448, true, 12, true),
                new VirtualTCPOptions(BasicProxyPort + 58, 1448, true, 13, true),
                new VirtualTCPOptions(BasicProxyPort + 59, 1448, true, 14, true),   //  37839

                new VirtualTCPOptions(BasicProxyPort + 60, 1444, true, 0, true),   //  37840
                new VirtualTCPOptions(BasicProxyPort + 61, 1444, true, 1, true),
                new VirtualTCPOptions(BasicProxyPort + 62, 1444, true, 2, true),
                new VirtualTCPOptions(BasicProxyPort + 63, 1444, true, 3, true),
                new VirtualTCPOptions(BasicProxyPort + 64, 1444, true, 4, true),
                new VirtualTCPOptions(BasicProxyPort + 65, 1444, true, 5, true),
                new VirtualTCPOptions(BasicProxyPort + 66, 1444, true, 6, true),
                new VirtualTCPOptions(BasicProxyPort + 67, 1444, true, 7, true),
                new VirtualTCPOptions(BasicProxyPort + 68, 1444, true, 8, true),
                new VirtualTCPOptions(BasicProxyPort + 69, 1444, true, 9, true),
                new VirtualTCPOptions(BasicProxyPort + 70, 1444, true, 10, true),
                new VirtualTCPOptions(BasicProxyPort + 71, 1444, true, 11, true),
                new VirtualTCPOptions(BasicProxyPort + 72, 1444, true, 12, true),
                new VirtualTCPOptions(BasicProxyPort + 73, 1444, true, 13, true),
                new VirtualTCPOptions(BasicProxyPort + 74, 1444, true, 14, true),   // 37854

                new VirtualTCPOptions(BasicProxyPort + 75, 1440, true, 0, true),   // 37855
                new VirtualTCPOptions(BasicProxyPort + 76, 1440, true, 1, true),
                new VirtualTCPOptions(BasicProxyPort + 77, 1440, true, 2, true),
                new VirtualTCPOptions(BasicProxyPort + 78, 1440, true, 3, true),
                new VirtualTCPOptions(BasicProxyPort + 79, 1440, true, 4, true),
                new VirtualTCPOptions(BasicProxyPort + 80, 1440, true, 5, true),
                new VirtualTCPOptions(BasicProxyPort + 81, 1440, true, 6, true),
                new VirtualTCPOptions(BasicProxyPort + 82, 1440, true, 7, true),
                new VirtualTCPOptions(BasicProxyPort + 83, 1440, true, 8, true),
                new VirtualTCPOptions(BasicProxyPort + 84, 1440, true, 9, true),
                new VirtualTCPOptions(BasicProxyPort + 85, 1440, true, 10, true),
                new VirtualTCPOptions(BasicProxyPort + 86, 1440, true, 11, true),
                new VirtualTCPOptions(BasicProxyPort + 87, 1440, true, 12, true),
                new VirtualTCPOptions(BasicProxyPort + 88, 1440, true, 13, true),
                new VirtualTCPOptions(BasicProxyPort + 89, 1440, true, 14, true),   // 37869

                new VirtualTCPOptions(BasicProxyPort + 90, 1436, true, 0, true),   // 37870
                new VirtualTCPOptions(BasicProxyPort + 91, 1436, true, 1, true),
                new VirtualTCPOptions(BasicProxyPort + 92, 1436, true, 2, true),
                new VirtualTCPOptions(BasicProxyPort + 93, 1436, true, 3, true),
                new VirtualTCPOptions(BasicProxyPort + 94, 1436, true, 4, true),
                new VirtualTCPOptions(BasicProxyPort + 95, 1436, true, 5, true),
                new VirtualTCPOptions(BasicProxyPort + 96, 1436, true, 6, true),
                new VirtualTCPOptions(BasicProxyPort + 97, 1436, true, 7, true),
                new VirtualTCPOptions(BasicProxyPort + 98, 1436, true, 8, true),
                new VirtualTCPOptions(BasicProxyPort + 99, 1436, true, 9, true),
                new VirtualTCPOptions(BasicProxyPort + 100, 1436, true, 10, true),
                new VirtualTCPOptions(BasicProxyPort + 101, 1436, true, 11, true),
                new VirtualTCPOptions(BasicProxyPort + 102, 1436, true, 12, true),
                new VirtualTCPOptions(BasicProxyPort + 103, 1436, true, 13, true),
                new VirtualTCPOptions(BasicProxyPort + 104, 1436, true, 14, true),   // 37884

                new VirtualTCPOptions(BasicProxyPort + 105, 1432, true, 0, true),   // 37885
                new VirtualTCPOptions(BasicProxyPort + 106, 1432, true, 1, true),
                new VirtualTCPOptions(BasicProxyPort + 107, 1432, true, 2, true),
                new VirtualTCPOptions(BasicProxyPort + 108, 1432, true, 3, true),
                new VirtualTCPOptions(BasicProxyPort + 109, 1432, true, 4, true),
                new VirtualTCPOptions(BasicProxyPort + 110, 1432, true, 5, true),
                new VirtualTCPOptions(BasicProxyPort + 111, 1432, true, 6, true),
                new VirtualTCPOptions(BasicProxyPort + 112, 1432, true, 7, true),
                new VirtualTCPOptions(BasicProxyPort + 113, 1432, true, 8, true),
                new VirtualTCPOptions(BasicProxyPort + 114, 1432, true, 9, true),
                new VirtualTCPOptions(BasicProxyPort + 115, 1432, true, 10, true),
                new VirtualTCPOptions(BasicProxyPort + 116, 1432, true, 11, true),
                new VirtualTCPOptions(BasicProxyPort + 117, 1432, true, 12, true),
                new VirtualTCPOptions(BasicProxyPort + 118, 1432, true, 13, true),
                new VirtualTCPOptions(BasicProxyPort + 119, 1432, true, 14, true),   // 37899

                new VirtualTCPOptions(BasicProxyPort + 120, 1428, true, 0, true),   // 37900
                new VirtualTCPOptions(BasicProxyPort + 121, 1428, true, 1, true),
                new VirtualTCPOptions(BasicProxyPort + 122, 1428, true, 2, true),
                new VirtualTCPOptions(BasicProxyPort + 123, 1428, true, 3, true),
                new VirtualTCPOptions(BasicProxyPort + 124, 1428, true, 4, true),
                new VirtualTCPOptions(BasicProxyPort + 125, 1428, true, 5, true),
                new VirtualTCPOptions(BasicProxyPort + 126, 1428, true, 6, true),
                new VirtualTCPOptions(BasicProxyPort + 127, 1428, true, 7, true),
                new VirtualTCPOptions(BasicProxyPort + 128, 1428, true, 8, true),
                new VirtualTCPOptions(BasicProxyPort + 129, 1428, true, 9, true),
                new VirtualTCPOptions(BasicProxyPort + 130, 1428, true, 10, true),
                new VirtualTCPOptions(BasicProxyPort + 131, 1428, true, 11, true),
                new VirtualTCPOptions(BasicProxyPort + 132, 1428, true, 12, true),
                new VirtualTCPOptions(BasicProxyPort + 133, 1428, true, 13, true),
                new VirtualTCPOptions(BasicProxyPort + 134, 1428, true, 14, true),   // 37914

                new VirtualTCPOptions(BasicProxyPort + 135, 1424, true, 0, true),   // 37915
                new VirtualTCPOptions(BasicProxyPort + 136, 1424, true, 1, true),
                new VirtualTCPOptions(BasicProxyPort + 137, 1424, true, 2, true),
                new VirtualTCPOptions(BasicProxyPort + 138, 1424, true, 3, true),
                new VirtualTCPOptions(BasicProxyPort + 139, 1424, true, 4, true),
                new VirtualTCPOptions(BasicProxyPort + 140, 1424, true, 5, true),
                new VirtualTCPOptions(BasicProxyPort + 141, 1424, true, 6, true),
                new VirtualTCPOptions(BasicProxyPort + 142, 1424, true, 7, true),
                new VirtualTCPOptions(BasicProxyPort + 143, 1424, true, 8, true),
                new VirtualTCPOptions(BasicProxyPort + 144, 1424, true, 9, true),
                new VirtualTCPOptions(BasicProxyPort + 145, 1424, true, 10, true),
                new VirtualTCPOptions(BasicProxyPort + 146, 1424, true, 11, true),
                new VirtualTCPOptions(BasicProxyPort + 147, 1424, true, 12, true),
                new VirtualTCPOptions(BasicProxyPort + 148, 1424, true, 13, true),
                new VirtualTCPOptions(BasicProxyPort + 149, 1424, true, 14, true),   // 37929

                new VirtualTCPOptions(BasicProxyPort + 150, 1420, true, 0, true),   // 37915
                new VirtualTCPOptions(BasicProxyPort + 151, 1420, true, 1, true),
                new VirtualTCPOptions(BasicProxyPort + 152, 1420, true, 2, true),
                new VirtualTCPOptions(BasicProxyPort + 153, 1420, true, 3, true),
                new VirtualTCPOptions(BasicProxyPort + 154, 1420, true, 4, true),
                new VirtualTCPOptions(BasicProxyPort + 155, 1420, true, 5, true),
                new VirtualTCPOptions(BasicProxyPort + 156, 1420, true, 6, true),
                new VirtualTCPOptions(BasicProxyPort + 157, 1420, true, 7, true),
                new VirtualTCPOptions(BasicProxyPort + 158, 1420, true, 8, true),
                new VirtualTCPOptions(BasicProxyPort + 159, 1420, true, 9, true),
                new VirtualTCPOptions(BasicProxyPort + 160, 1420, true, 10, true),
                new VirtualTCPOptions(BasicProxyPort + 161, 1420, true, 11, true),
                new VirtualTCPOptions(BasicProxyPort + 162, 1420, true, 12, true),
                new VirtualTCPOptions(BasicProxyPort + 163, 1420, true, 13, true),
                new VirtualTCPOptions(BasicProxyPort + 164, 1420, true, 14, true),   // 37944

                new VirtualTCPOptions(BasicProxyPort + 165, 1400, true, 0, true),   // 37945
                new VirtualTCPOptions(BasicProxyPort + 166, 1400, true, 1, true),
                new VirtualTCPOptions(BasicProxyPort + 167, 1400, true, 2, true),
                new VirtualTCPOptions(BasicProxyPort + 168, 1400, true, 3, true),
                new VirtualTCPOptions(BasicProxyPort + 169, 1400, true, 4, true),
                new VirtualTCPOptions(BasicProxyPort + 170, 1400, true, 5, true),
                new VirtualTCPOptions(BasicProxyPort + 171, 1400, true, 6, true),
                new VirtualTCPOptions(BasicProxyPort + 172, 1400, true, 7, true),
                new VirtualTCPOptions(BasicProxyPort + 173, 1400, true, 8, true),
                new VirtualTCPOptions(BasicProxyPort + 174, 1400, true, 9, true),
                new VirtualTCPOptions(BasicProxyPort + 175, 1400, true, 10, true),
                new VirtualTCPOptions(BasicProxyPort + 176, 1400, true, 11, true),
                new VirtualTCPOptions(BasicProxyPort + 177, 1400, true, 12, true),
                new VirtualTCPOptions(BasicProxyPort + 178, 1400, true, 13, true),
                new VirtualTCPOptions(BasicProxyPort + 179, 1400, true, 14, true),   // 37959

                new VirtualTCPOptions(BasicProxyPort + 180, 1220, true, 0, true),   // 37960
                new VirtualTCPOptions(BasicProxyPort + 181, 1220, true, 1, true),
                new VirtualTCPOptions(BasicProxyPort + 182, 1220, true, 2, true),
                new VirtualTCPOptions(BasicProxyPort + 183, 1220, true, 3, true),
                new VirtualTCPOptions(BasicProxyPort + 184, 1220, true, 4, true),
                new VirtualTCPOptions(BasicProxyPort + 185, 1220, true, 5, true),
                new VirtualTCPOptions(BasicProxyPort + 186, 1220, true, 6, true),
                new VirtualTCPOptions(BasicProxyPort + 187, 1220, true, 7, true),
                new VirtualTCPOptions(BasicProxyPort + 188, 1220, true, 8, true),
                new VirtualTCPOptions(BasicProxyPort + 189, 1220, true, 9, true),
                new VirtualTCPOptions(BasicProxyPort + 190, 1220, true, 10, true),
                new VirtualTCPOptions(BasicProxyPort + 191, 1220, true, 11, true),
                new VirtualTCPOptions(BasicProxyPort + 192, 1220, true, 12, true),
                new VirtualTCPOptions(BasicProxyPort + 193, 1220, true, 13, true),
                new VirtualTCPOptions(BasicProxyPort + 194, 1220, true, 14, true),   // 37974

                new VirtualTCPOptions(BasicProxyPort + 195, 1024, true, 0, true),   // 37975
                new VirtualTCPOptions(BasicProxyPort + 196, 1024, true, 1, true),
                new VirtualTCPOptions(BasicProxyPort + 197, 1024, true, 2, true),
                new VirtualTCPOptions(BasicProxyPort + 198, 1024, true, 3, true),
                new VirtualTCPOptions(BasicProxyPort + 199, 1024, true, 4, true),
                new VirtualTCPOptions(BasicProxyPort + 200, 1024, true, 5, true),
                new VirtualTCPOptions(BasicProxyPort + 201, 1024, true, 6, true),
                new VirtualTCPOptions(BasicProxyPort + 202, 1024, true, 7, true),
                new VirtualTCPOptions(BasicProxyPort + 203, 1024, true, 8, true),
                new VirtualTCPOptions(BasicProxyPort + 204, 1024, true, 9, true),
                new VirtualTCPOptions(BasicProxyPort + 205, 1024, true, 10, true),
                new VirtualTCPOptions(BasicProxyPort + 206, 1024, true, 11, true),
                new VirtualTCPOptions(BasicProxyPort + 207, 1024, true, 12, true),
                new VirtualTCPOptions(BasicProxyPort + 208, 1024, true, 13, true),
                new VirtualTCPOptions(BasicProxyPort + 209, 1024, true, 14, true),   // 37989

                new VirtualTCPOptions(BasicProxyPort + 210, 536, true, 0, true),   // 37990
                new VirtualTCPOptions(BasicProxyPort + 211, 536, true, 1, true),
                new VirtualTCPOptions(BasicProxyPort + 212, 536, true, 2, true),
                new VirtualTCPOptions(BasicProxyPort + 213, 536, true, 3, true),
                new VirtualTCPOptions(BasicProxyPort + 214, 536, true, 4, true),
                new VirtualTCPOptions(BasicProxyPort + 215, 536, true, 5, true),
                new VirtualTCPOptions(BasicProxyPort + 216, 536, true, 6, true),
                new VirtualTCPOptions(BasicProxyPort + 217, 536, true, 7, true),
                new VirtualTCPOptions(BasicProxyPort + 218, 536, true, 8, true),
                new VirtualTCPOptions(BasicProxyPort + 219, 536, true, 9, true),
                new VirtualTCPOptions(BasicProxyPort + 220, 536, true, 10, true),
                new VirtualTCPOptions(BasicProxyPort + 221, 536, true, 11, true),
                new VirtualTCPOptions(BasicProxyPort + 222, 536, true, 12, true),
                new VirtualTCPOptions(BasicProxyPort + 223, 536, true, 13, true),
                new VirtualTCPOptions(BasicProxyPort + 224, 536, true, 14, true),   // 38004

                // Window scale supported. SACK not permitted.

                new VirtualTCPOptions(BasicProxyPort + 225, 1460, true, 0, false),  //   38005
                new VirtualTCPOptions(BasicProxyPort + 226, 1460, true, 1, false),
                new VirtualTCPOptions(BasicProxyPort + 227, 1460, true, 2, false),
                new VirtualTCPOptions(BasicProxyPort + 228, 1460, true, 3, false),
                new VirtualTCPOptions(BasicProxyPort + 229, 1460, true, 4, false),
                new VirtualTCPOptions(BasicProxyPort + 230, 1460, true, 5, false),
                new VirtualTCPOptions(BasicProxyPort + 231, 1460, true, 6, false),
                new VirtualTCPOptions(BasicProxyPort + 232, 1460, true, 7, false),
                new VirtualTCPOptions(BasicProxyPort + 233, 1460, true, 8, false), 
                new VirtualTCPOptions(BasicProxyPort + 234, 1460, true, 9, false),
                new VirtualTCPOptions(BasicProxyPort + 235, 1460, true, 10, false),
                new VirtualTCPOptions(BasicProxyPort + 236, 1460, true, 11, false),
                new VirtualTCPOptions(BasicProxyPort + 237, 1460, true, 12, false),
                new VirtualTCPOptions(BasicProxyPort + 238, 1460, true, 13, false),
                new VirtualTCPOptions(BasicProxyPort + 239, 1460, true, 14, false),  //  38019

                new VirtualTCPOptions(BasicProxyPort + 240, 1456, true, 0, false),  //   38020
                new VirtualTCPOptions(BasicProxyPort + 241, 1456, true, 1, false),
                new VirtualTCPOptions(BasicProxyPort + 242, 1456, true, 2, false),
                new VirtualTCPOptions(BasicProxyPort + 243, 1456, true, 3, false),
                new VirtualTCPOptions(BasicProxyPort + 244, 1456, true, 4, false),
                new VirtualTCPOptions(BasicProxyPort + 245, 1456, true, 5, false),
                new VirtualTCPOptions(BasicProxyPort + 246, 1456, true, 6, false),
                new VirtualTCPOptions(BasicProxyPort + 247, 1456, true, 7, false),
                new VirtualTCPOptions(BasicProxyPort + 248, 1456, true, 8, false), 
                new VirtualTCPOptions(BasicProxyPort + 249, 1456, true, 9, false),
                new VirtualTCPOptions(BasicProxyPort + 250, 1456, true, 10, false),
                new VirtualTCPOptions(BasicProxyPort + 251, 1456, true, 11, false),
                new VirtualTCPOptions(BasicProxyPort + 252, 1456, true, 12, false),
                new VirtualTCPOptions(BasicProxyPort + 253, 1456, true, 13, false),
                new VirtualTCPOptions(BasicProxyPort + 254, 1456, true, 14, false),  //  38034

                new VirtualTCPOptions(BasicProxyPort + 255, 1452, true, 0, false),  //   38035
                new VirtualTCPOptions(BasicProxyPort + 256, 1452, true, 1, false),
                new VirtualTCPOptions(BasicProxyPort + 257, 1452, true, 2, false),
                new VirtualTCPOptions(BasicProxyPort + 258, 1452, true, 3, false),
                new VirtualTCPOptions(BasicProxyPort + 259, 1452, true, 4, false),
                new VirtualTCPOptions(BasicProxyPort + 260, 1452, true, 5, false),
                new VirtualTCPOptions(BasicProxyPort + 261, 1452, true, 6, false),
                new VirtualTCPOptions(BasicProxyPort + 262, 1452, true, 7, false),
                new VirtualTCPOptions(BasicProxyPort + 263, 1452, true, 8, false), 
                new VirtualTCPOptions(BasicProxyPort + 264, 1452, true, 9, false),
                new VirtualTCPOptions(BasicProxyPort + 265, 1452, true, 10, false),
                new VirtualTCPOptions(BasicProxyPort + 266, 1452, true, 11, false),
                new VirtualTCPOptions(BasicProxyPort + 267, 1452, true, 12, false),
                new VirtualTCPOptions(BasicProxyPort + 268, 1452, true, 13, false),
                new VirtualTCPOptions(BasicProxyPort + 269, 1452, true, 14, false),  //  38049

                new VirtualTCPOptions(BasicProxyPort + 270, 1448, true, 0, false),  //   38050
                new VirtualTCPOptions(BasicProxyPort + 271, 1448, true, 1, false),
                new VirtualTCPOptions(BasicProxyPort + 272, 1448, true, 2, false),
                new VirtualTCPOptions(BasicProxyPort + 273, 1448, true, 3, false),
                new VirtualTCPOptions(BasicProxyPort + 274, 1448, true, 4, false),
                new VirtualTCPOptions(BasicProxyPort + 275, 1448, true, 5, false),
                new VirtualTCPOptions(BasicProxyPort + 276, 1448, true, 6, false),
                new VirtualTCPOptions(BasicProxyPort + 277, 1448, true, 7, false),
                new VirtualTCPOptions(BasicProxyPort + 278, 1448, true, 8, false),
                new VirtualTCPOptions(BasicProxyPort + 279, 1448, true, 9, false),
                new VirtualTCPOptions(BasicProxyPort + 280, 1448, true, 10, false),
                new VirtualTCPOptions(BasicProxyPort + 281, 1448, true, 11, false),
                new VirtualTCPOptions(BasicProxyPort + 282, 1448, true, 12, false),
                new VirtualTCPOptions(BasicProxyPort + 283, 1448, true, 13, false),
                new VirtualTCPOptions(BasicProxyPort + 284, 1448, true, 14, false),  //  38064

                new VirtualTCPOptions(BasicProxyPort + 285, 1444, true, 0, false),  //   38065
                new VirtualTCPOptions(BasicProxyPort + 286, 1444, true, 1, false),
                new VirtualTCPOptions(BasicProxyPort + 287, 1444, true, 2, false),
                new VirtualTCPOptions(BasicProxyPort + 288, 1444, true, 3, false),
                new VirtualTCPOptions(BasicProxyPort + 289, 1444, true, 4, false),
                new VirtualTCPOptions(BasicProxyPort + 290, 1444, true, 5, false),
                new VirtualTCPOptions(BasicProxyPort + 291, 1444, true, 6, false),
                new VirtualTCPOptions(BasicProxyPort + 292, 1444, true, 7, false),
                new VirtualTCPOptions(BasicProxyPort + 293, 1444, true, 8, false),
                new VirtualTCPOptions(BasicProxyPort + 294, 1444, true, 9, false),
                new VirtualTCPOptions(BasicProxyPort + 295, 1444, true, 10, false),
                new VirtualTCPOptions(BasicProxyPort + 296, 1444, true, 11, false),
                new VirtualTCPOptions(BasicProxyPort + 297, 1444, true, 12, false),
                new VirtualTCPOptions(BasicProxyPort + 298, 1444, true, 13, false),
                new VirtualTCPOptions(BasicProxyPort + 299, 1444, true, 14, false),  //  38079

                new VirtualTCPOptions(BasicProxyPort + 300, 1440, true, 0, false),  //   38080
                new VirtualTCPOptions(BasicProxyPort + 301, 1440, true, 1, false),
                new VirtualTCPOptions(BasicProxyPort + 302, 1440, true, 2, false),
                new VirtualTCPOptions(BasicProxyPort + 303, 1440, true, 3, false),
                new VirtualTCPOptions(BasicProxyPort + 304, 1440, true, 4, false),
                new VirtualTCPOptions(BasicProxyPort + 305, 1440, true, 5, false),
                new VirtualTCPOptions(BasicProxyPort + 306, 1440, true, 6, false),
                new VirtualTCPOptions(BasicProxyPort + 307, 1440, true, 7, false),
                new VirtualTCPOptions(BasicProxyPort + 308, 1440, true, 8, false),
                new VirtualTCPOptions(BasicProxyPort + 309, 1440, true, 9, false),
                new VirtualTCPOptions(BasicProxyPort + 310, 1440, true, 10, false),
                new VirtualTCPOptions(BasicProxyPort + 311, 1440, true, 11, false),
                new VirtualTCPOptions(BasicProxyPort + 312, 1440, true, 12, false),
                new VirtualTCPOptions(BasicProxyPort + 313, 1440, true, 13, false),
                new VirtualTCPOptions(BasicProxyPort + 314, 1440, true, 14, false),  //  38094

                new VirtualTCPOptions(BasicProxyPort + 315, 1436, true, 0, false),  //   38095
                new VirtualTCPOptions(BasicProxyPort + 316, 1436, true, 1, false),
                new VirtualTCPOptions(BasicProxyPort + 317, 1436, true, 2, false),
                new VirtualTCPOptions(BasicProxyPort + 318, 1436, true, 3, false),
                new VirtualTCPOptions(BasicProxyPort + 319, 1436, true, 4, false),
                new VirtualTCPOptions(BasicProxyPort + 320, 1436, true, 5, false),
                new VirtualTCPOptions(BasicProxyPort + 321, 1436, true, 6, false),
                new VirtualTCPOptions(BasicProxyPort + 322, 1436, true, 7, false),
                new VirtualTCPOptions(BasicProxyPort + 323, 1436, true, 8, false),
                new VirtualTCPOptions(BasicProxyPort + 324, 1436, true, 9, false),
                new VirtualTCPOptions(BasicProxyPort + 325, 1436, true, 10, false),
                new VirtualTCPOptions(BasicProxyPort + 326, 1436, true, 11, false),
                new VirtualTCPOptions(BasicProxyPort + 327, 1436, true, 12, false),
                new VirtualTCPOptions(BasicProxyPort + 328, 1436, true, 13, false),
                new VirtualTCPOptions(BasicProxyPort + 329, 1436, true, 14, false),  //  38109

                new VirtualTCPOptions(BasicProxyPort + 330, 1432, true, 0, false),  //   38110
                new VirtualTCPOptions(BasicProxyPort + 331, 1432, true, 1, false),
                new VirtualTCPOptions(BasicProxyPort + 332, 1432, true, 2, false),
                new VirtualTCPOptions(BasicProxyPort + 333, 1432, true, 3, false),
                new VirtualTCPOptions(BasicProxyPort + 334, 1432, true, 4, false),
                new VirtualTCPOptions(BasicProxyPort + 335, 1432, true, 5, false),
                new VirtualTCPOptions(BasicProxyPort + 336, 1432, true, 6, false),
                new VirtualTCPOptions(BasicProxyPort + 337, 1432, true, 7, false),
                new VirtualTCPOptions(BasicProxyPort + 338, 1432, true, 8, false),
                new VirtualTCPOptions(BasicProxyPort + 339, 1432, true, 9, false),
                new VirtualTCPOptions(BasicProxyPort + 340, 1432, true, 10, false),
                new VirtualTCPOptions(BasicProxyPort + 341, 1432, true, 11, false),
                new VirtualTCPOptions(BasicProxyPort + 342, 1432, true, 12, false),
                new VirtualTCPOptions(BasicProxyPort + 343, 1432, true, 13, false),
                new VirtualTCPOptions(BasicProxyPort + 344, 1432, true, 14, false),  //  38124

                new VirtualTCPOptions(BasicProxyPort + 345, 1428, true, 0, false),  //   38125
                new VirtualTCPOptions(BasicProxyPort + 346, 1428, true, 1, false),
                new VirtualTCPOptions(BasicProxyPort + 347, 1428, true, 2, false),
                new VirtualTCPOptions(BasicProxyPort + 348, 1428, true, 3, false),
                new VirtualTCPOptions(BasicProxyPort + 349, 1428, true, 4, false),
                new VirtualTCPOptions(BasicProxyPort + 350, 1428, true, 5, false),
                new VirtualTCPOptions(BasicProxyPort + 351, 1428, true, 6, false),
                new VirtualTCPOptions(BasicProxyPort + 352, 1428, true, 7, false),
                new VirtualTCPOptions(BasicProxyPort + 353, 1428, true, 8, false),
                new VirtualTCPOptions(BasicProxyPort + 354, 1428, true, 9, false),
                new VirtualTCPOptions(BasicProxyPort + 355, 1428, true, 10, false),
                new VirtualTCPOptions(BasicProxyPort + 356, 1428, true, 11, false),
                new VirtualTCPOptions(BasicProxyPort + 357, 1428, true, 12, false),
                new VirtualTCPOptions(BasicProxyPort + 358, 1428, true, 13, false),
                new VirtualTCPOptions(BasicProxyPort + 359, 1428, true, 14, false),  //  38139

                new VirtualTCPOptions(BasicProxyPort + 360, 1424, true, 0, false),  //   38140
                new VirtualTCPOptions(BasicProxyPort + 361, 1424, true, 1, false),
                new VirtualTCPOptions(BasicProxyPort + 362, 1424, true, 2, false),
                new VirtualTCPOptions(BasicProxyPort + 363, 1424, true, 3, false),
                new VirtualTCPOptions(BasicProxyPort + 364, 1424, true, 4, false),
                new VirtualTCPOptions(BasicProxyPort + 365, 1424, true, 5, false),
                new VirtualTCPOptions(BasicProxyPort + 366, 1424, true, 6, false),
                new VirtualTCPOptions(BasicProxyPort + 367, 1424, true, 7, false),
                new VirtualTCPOptions(BasicProxyPort + 368, 1424, true, 8, false),
                new VirtualTCPOptions(BasicProxyPort + 369, 1424, true, 9, false),
                new VirtualTCPOptions(BasicProxyPort + 370, 1424, true, 10, false),
                new VirtualTCPOptions(BasicProxyPort + 371, 1424, true, 11, false),
                new VirtualTCPOptions(BasicProxyPort + 372, 1424, true, 12, false),
                new VirtualTCPOptions(BasicProxyPort + 373, 1424, true, 13, false),
                new VirtualTCPOptions(BasicProxyPort + 374, 1424, true, 14, false),  //  38154

                new VirtualTCPOptions(BasicProxyPort + 375, 1420, true, 0, false),  //   38155
                new VirtualTCPOptions(BasicProxyPort + 376, 1420, true, 1, false),
                new VirtualTCPOptions(BasicProxyPort + 377, 1420, true, 2, false),
                new VirtualTCPOptions(BasicProxyPort + 378, 1420, true, 3, false),
                new VirtualTCPOptions(BasicProxyPort + 379, 1420, true, 4, false),
                new VirtualTCPOptions(BasicProxyPort + 380, 1420, true, 5, false),
                new VirtualTCPOptions(BasicProxyPort + 381, 1420, true, 6, false),
                new VirtualTCPOptions(BasicProxyPort + 382, 1420, true, 7, false),
                new VirtualTCPOptions(BasicProxyPort + 383, 1420, true, 8, false),
                new VirtualTCPOptions(BasicProxyPort + 384, 1420, true, 9, false),
                new VirtualTCPOptions(BasicProxyPort + 385, 1420, true, 10, false),
                new VirtualTCPOptions(BasicProxyPort + 386, 1420, true, 11, false),
                new VirtualTCPOptions(BasicProxyPort + 387, 1420, true, 12, false),
                new VirtualTCPOptions(BasicProxyPort + 388, 1420, true, 13, false),
                new VirtualTCPOptions(BasicProxyPort + 389, 1420, true, 14, false),  //  38169

                new VirtualTCPOptions(BasicProxyPort + 390, 1400, true, 0, false),  //   38170
                new VirtualTCPOptions(BasicProxyPort + 391, 1400, true, 1, false),
                new VirtualTCPOptions(BasicProxyPort + 392, 1400, true, 2, false),
                new VirtualTCPOptions(BasicProxyPort + 393, 1400, true, 3, false),
                new VirtualTCPOptions(BasicProxyPort + 394, 1400, true, 4, false),
                new VirtualTCPOptions(BasicProxyPort + 395, 1400, true, 5, false),
                new VirtualTCPOptions(BasicProxyPort + 396, 1400, true, 6, false),
                new VirtualTCPOptions(BasicProxyPort + 397, 1400, true, 7, false),
                new VirtualTCPOptions(BasicProxyPort + 398, 1400, true, 8, false),
                new VirtualTCPOptions(BasicProxyPort + 399, 1400, true, 9, false),
                new VirtualTCPOptions(BasicProxyPort + 400, 1400, true, 10, false),
                new VirtualTCPOptions(BasicProxyPort + 401, 1400, true, 11, false),
                new VirtualTCPOptions(BasicProxyPort + 402, 1400, true, 12, false),
                new VirtualTCPOptions(BasicProxyPort + 403, 1400, true, 13, false),
                new VirtualTCPOptions(BasicProxyPort + 404, 1400, true, 14, false),  //  38184

                new VirtualTCPOptions(BasicProxyPort + 405, 1220, true, 0, false),  //   38185
                new VirtualTCPOptions(BasicProxyPort + 406, 1220, true, 1, false),
                new VirtualTCPOptions(BasicProxyPort + 407, 1220, true, 2, false),
                new VirtualTCPOptions(BasicProxyPort + 408, 1220, true, 3, false),
                new VirtualTCPOptions(BasicProxyPort + 409, 1220, true, 4, false),
                new VirtualTCPOptions(BasicProxyPort + 410, 1220, true, 5, false),
                new VirtualTCPOptions(BasicProxyPort + 411, 1220, true, 6, false),
                new VirtualTCPOptions(BasicProxyPort + 412, 1220, true, 7, false),
                new VirtualTCPOptions(BasicProxyPort + 413, 1220, true, 8, false),
                new VirtualTCPOptions(BasicProxyPort + 414, 1220, true, 9, false),
                new VirtualTCPOptions(BasicProxyPort + 415, 1220, true, 10, false),
                new VirtualTCPOptions(BasicProxyPort + 416, 1220, true, 11, false),
                new VirtualTCPOptions(BasicProxyPort + 417, 1220, true, 12, false),
                new VirtualTCPOptions(BasicProxyPort + 418, 1220, true, 13, false),
                new VirtualTCPOptions(BasicProxyPort + 419, 1220, true, 14, false),  //  38199

                new VirtualTCPOptions(BasicProxyPort + 420, 1024, true, 0, false),  //   38200
                new VirtualTCPOptions(BasicProxyPort + 421, 1024, true, 1, false),
                new VirtualTCPOptions(BasicProxyPort + 422, 1024, true, 2, false),
                new VirtualTCPOptions(BasicProxyPort + 423, 1024, true, 3, false),
                new VirtualTCPOptions(BasicProxyPort + 424, 1024, true, 4, false),
                new VirtualTCPOptions(BasicProxyPort + 425, 1024, true, 5, false),
                new VirtualTCPOptions(BasicProxyPort + 426, 1024, true, 6, false),
                new VirtualTCPOptions(BasicProxyPort + 427, 1024, true, 7, false),
                new VirtualTCPOptions(BasicProxyPort + 428, 1024, true, 8, false),
                new VirtualTCPOptions(BasicProxyPort + 429, 1024, true, 9, false),
                new VirtualTCPOptions(BasicProxyPort + 430, 1024, true, 10, false),
                new VirtualTCPOptions(BasicProxyPort + 431, 1024, true, 11, false),
                new VirtualTCPOptions(BasicProxyPort + 432, 1024, true, 12, false),
                new VirtualTCPOptions(BasicProxyPort + 433, 1024, true, 13, false),
                new VirtualTCPOptions(BasicProxyPort + 434, 1024, true, 14, false),  //  38214

                new VirtualTCPOptions(BasicProxyPort + 435, 536, true, 0, false),  //   38215
                new VirtualTCPOptions(BasicProxyPort + 436, 536, true, 1, false),
                new VirtualTCPOptions(BasicProxyPort + 437, 536, true, 2, false),
                new VirtualTCPOptions(BasicProxyPort + 438, 536, true, 3, false),
                new VirtualTCPOptions(BasicProxyPort + 439, 536, true, 4, false),
                new VirtualTCPOptions(BasicProxyPort + 440, 536, true, 5, false),
                new VirtualTCPOptions(BasicProxyPort + 441, 536, true, 6, false),
                new VirtualTCPOptions(BasicProxyPort + 442, 536, true, 7, false),
                new VirtualTCPOptions(BasicProxyPort + 443, 536, true, 8, false),
                new VirtualTCPOptions(BasicProxyPort + 444, 536, true, 9, false),
                new VirtualTCPOptions(BasicProxyPort + 445, 536, true, 10, false),
                new VirtualTCPOptions(BasicProxyPort + 446, 536, true, 11, false),
                new VirtualTCPOptions(BasicProxyPort + 447, 536, true, 12, false),
                new VirtualTCPOptions(BasicProxyPort + 448, 536, true, 13, false),
                new VirtualTCPOptions(BasicProxyPort + 449, 536, true, 14, false),  //  38229

                // Window scale not supported. SACK permitted.

                new VirtualTCPOptions(BasicProxyPort + 450, 1460, false, 0, true),  //  38230
                new VirtualTCPOptions(BasicProxyPort + 451, 1456, false, 0, true),
                new VirtualTCPOptions(BasicProxyPort + 452, 1452, false, 0, true),
                new VirtualTCPOptions(BasicProxyPort + 453, 1448, false, 0, true),
                new VirtualTCPOptions(BasicProxyPort + 454, 1444, false, 0, true),
                new VirtualTCPOptions(BasicProxyPort + 455, 1440, false, 0, true),
                new VirtualTCPOptions(BasicProxyPort + 456, 1436, false, 0, true),
                new VirtualTCPOptions(BasicProxyPort + 457, 1432, false, 0, true),
                new VirtualTCPOptions(BasicProxyPort + 458, 1428, false, 0, true),
                new VirtualTCPOptions(BasicProxyPort + 459, 1424, false, 0, true),
                new VirtualTCPOptions(BasicProxyPort + 460, 1420, false, 0, true),
                new VirtualTCPOptions(BasicProxyPort + 461, 1400, false, 0, true),
                new VirtualTCPOptions(BasicProxyPort + 462, 1220, false, 0, true),
                new VirtualTCPOptions(BasicProxyPort + 463, 1024, false, 0, true),
                new VirtualTCPOptions(BasicProxyPort + 464, 536, false, 0, true),   //  38244

                // Window scale not supported. SACK not permitted.

                new VirtualTCPOptions(BasicProxyPort + 465, 1460, false, 0, false),  //  38245
                new VirtualTCPOptions(BasicProxyPort + 466, 1456, false, 0, false),
                new VirtualTCPOptions(BasicProxyPort + 467, 1452, false, 0, false),
                new VirtualTCPOptions(BasicProxyPort + 468, 1448, false, 0, false),
                new VirtualTCPOptions(BasicProxyPort + 469, 1444, false, 0, false),
                new VirtualTCPOptions(BasicProxyPort + 470, 1440, false, 0, false),
                new VirtualTCPOptions(BasicProxyPort + 471, 1436, false, 0, false),
                new VirtualTCPOptions(BasicProxyPort + 472, 1432, false, 0, false),
                new VirtualTCPOptions(BasicProxyPort + 473, 1428, false, 0, false),
                new VirtualTCPOptions(BasicProxyPort + 474, 1424, false, 0, false),
                new VirtualTCPOptions(BasicProxyPort + 475, 1420, false, 0, false),
                new VirtualTCPOptions(BasicProxyPort + 476, 1400, false, 0, false),
                new VirtualTCPOptions(BasicProxyPort + 477, 1220, false, 0, false),
                new VirtualTCPOptions(BasicProxyPort + 478, 1024, false, 0, false),
                new VirtualTCPOptions(BasicProxyPort + 479, 536, false, 0, false)   //  38259
            };
        }
    }
}
