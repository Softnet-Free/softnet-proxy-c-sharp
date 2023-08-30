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
    static class UdpDispatcher
    {
        public static byte server_port_lower_byte;
        public static byte server_port_higher_byte;

        static LinkedList<UdpControl> UDPControlList;
        static LinkedList<UdpConnectorV6> ConnectorV6List;
        static LinkedList<UdpConnectorV4> ConnectorV4List;

        static Dictionary<IEPv6Key, UdpProxyV6> ProxyV6List;
        static Dictionary<long, UdpProxyV4> ProxyV4List;

        public static void Init()
        {
            UDPControlList = new LinkedList<UdpControl>();
            ConnectorV6List = new LinkedList<UdpConnectorV6>();
            ConnectorV4List = new LinkedList<UdpConnectorV4>();
            ProxyV6List = new Dictionary<IEPv6Key, UdpProxyV6>();
            ProxyV4List = new Dictionary<long, UdpProxyV4>();

            byte[] port_bytes = ByteConverter.GetBytes((ushort)Constants.UdpRzvPort);
            server_port_higher_byte = port_bytes[0];
            server_port_lower_byte = port_bytes[1];

            ScheduledTask task = new ScheduledTask(RemoveExpiredItems, null);
            TaskScheduler.Add(task, 60);
        }

        public static void Clear()
        {
            lock (ConnectorV6List)
            {
                foreach (UdpConnectorV6 connector in ConnectorV6List)
                    connector.Close();
            }

            lock (ConnectorV4List)
            {
                foreach (UdpConnectorV4 connector in ConnectorV4List)
                    connector.Close();
            }
        }

        static void RemoveExpiredItems(object noData)
        {
            long currentTime = SystemClock.Seconds;

            lock (UDPControlList)
            {
                while (UDPControlList.Count > 0)
                {
                    if (UDPControlList.Last.Value.deathTime <= currentTime)
                        UDPControlList.RemoveLast();
                    else
                        break;
                }
            }

            lock (ConnectorV6List)
            {
                while (ConnectorV6List.Count > 0)
                {
                    if (ConnectorV6List.Last.Value.deathTime <= currentTime)
                    {
                        if (ConnectorV6List.Last.Value.Completed == false)
                            ConnectorV6List.Last.Value.Terminate();
                        ConnectorV6List.RemoveLast();
                    }
                    else
                        break;
                }
            }

            lock (ConnectorV4List)
            {
                while (ConnectorV4List.Count > 0)
                {
                    if (ConnectorV4List.Last.Value.deathTime <= currentTime)
                    {
                        if (ConnectorV4List.Last.Value.Completed == false)
                            ConnectorV4List.Last.Value.Terminate();
                        ConnectorV4List.RemoveLast();
                    }
                    else
                        break;
                }
            }

            ScheduledTask task = new ScheduledTask(RemoveExpiredItems, null);
            TaskScheduler.Add(task, 10);
        }

        public static UdpControl GetControl(Guid connectionUid)
        {
            lock (UDPControlList)
            {
                if (UDPControlList.Count > 0)
                {
                    LinkedListNode<UdpControl> node = UDPControlList.First;
                    while (node != null)
                    {
                        if (node.Value.connectionUid.Equals(connectionUid))
                            return node.Value;
                        node = node.Next;
                    }
                }

                UdpControl control = new UdpControl(connectionUid);
                UDPControlList.AddFirst(control);

                return control;
            }
        }

        public static UdpControl FindControl(Guid connectionUid)
        {
            lock (UDPControlList)
            {
                if (UDPControlList.Count > 0)
                {
                    LinkedListNode<UdpControl> node = UDPControlList.First;
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

        public static void RegisterConnector(UdpConnectorV6 connector)
        {
            lock (ConnectorV6List)
            {
                ConnectorV6List.AddFirst(connector);
            }
        }

        public static void RegisterProxyV6(IEPv6Key endpointKey, UdpProxyV6 udpProxy)
        {
            lock (ProxyV6List)
            {
                UdpProxyV6 existedProxy = null;
                if (ProxyV6List.TryGetValue(endpointKey, out existedProxy))
                {
                    ProxyV6List.Remove(endpointKey);
                    existedProxy.Close();
                }
                ProxyV6List.Add(endpointKey, udpProxy);
            }
        }

        public static void RemoveProxyV6(UdpProxyV6 caller, IEPv6Key endpointKey)
        {
            lock (ProxyV6List)
            {
                UdpProxyV6 registeredProxy = null;
                if (ProxyV6List.TryGetValue(endpointKey, out registeredProxy))
                {
                    if (caller.Equals(registeredProxy))
                        ProxyV6List.Remove(endpointKey);
                }
            }
        }

        public static bool HandlePacketV6(SocketAsyncEventArgs saea)
        {
            byte[] buffer = saea.Buffer;
            int udpOffset = saea.Offset;
            int udpSize = saea.BytesTransferred;

            if (udpSize == 25 && buffer[udpOffset + 8] == Constants.UdpEndpoint.ATTACH_TO_CONNECTOR)
            {
                Guid endpointUid = ByteConverter.ToGuid(buffer, udpOffset + 9);
                UdpConnectorV6 connector = null;
                lock (ConnectorV6List)
                {
                    if (ConnectorV6List.Count > 0)
                    {
                        LinkedListNode<UdpConnectorV6> node = ConnectorV6List.First;
                        while (node != null)
                        {
                            if (node.Value.EndpointUid.Equals(endpointUid))
                            {
                                connector = node.Value;
                                break;
                            }
                            node = node.Next;
                        }
                    }
                }

                if (connector != null)
                {
                    connector.AttachEndpoint(saea);

                    buffer[udpOffset + 2] = buffer[udpOffset];
                    buffer[udpOffset + 3] = buffer[udpOffset + 1];
                    buffer[udpOffset] = server_port_higher_byte;
                    buffer[udpOffset + 1] = server_port_lower_byte;

                    buffer[udpOffset + 4] = 0;
                    buffer[udpOffset + 5] = 25;

                    buffer[udpOffset + 6] = 0;
                    buffer[udpOffset + 7] = 0;

                    buffer[udpOffset + 8] = Constants.UdpEndpoint.ATTACHED;

                    IPEndPoint remoteIEP = (IPEndPoint)saea.RemoteEndPoint;
                    byte[] pseudoHeader = UdpPseudoHeader.GetPHv6(remoteIEP.Address);

                    UInt16 checksum = Algorithms.ChecksumUdpV6(pseudoHeader, buffer, udpOffset, 25);
                    Buffer.BlockCopy(ByteConverter.GetBytes(checksum), 0, buffer, udpOffset + 6, 2);

                    saea.SetBuffer(udpOffset, 25);
                    RawUdpBindingV6.Send(saea);

                    return true;
                }
            }

            IPEndPoint hostIEP = (IPEndPoint)saea.RemoteEndPoint;
            byte[] hostIep = new byte[18];
            Buffer.BlockCopy(hostIEP.Address.GetAddressBytes(), 0, hostIep, 0, 16);
            Buffer.BlockCopy(saea.Buffer, saea.Offset, hostIep, 16, 2);
            IEPv6Key endpointKey = new IEPv6Key(hostIep);

            UdpProxyV6 udpProxy = null;
            lock (ProxyV6List)
            {
                ProxyV6List.TryGetValue(endpointKey, out udpProxy);
            }

            if (udpProxy != null)
            {
                udpProxy.HandlePacket(saea);
                return true;
            }

            return false;
        }

        #endregion

        #region ---------- Handling IPv4 packets --------------------------------------

        public static void RegisterConnector(UdpConnectorV4 connector)
        {
            lock (ConnectorV4List)
            {
                ConnectorV4List.AddFirst(connector);
            }
        }

        public static void RegisterProxyV4(long endpointKey, UdpProxyV4 udpProxy)
        {
            lock (ProxyV4List)
            {
                UdpProxyV4 existedProxy = null;
                if (ProxyV4List.TryGetValue(endpointKey, out existedProxy))
                {
                    ProxyV4List.Remove(endpointKey);
                    existedProxy.Close();
                }
                ProxyV4List.Add(endpointKey, udpProxy);
            }
        }

        public static void RemoveProxyV4(UdpProxyV4 caller, long endpointKey)
        {
            lock (ProxyV4List)
            {
                UdpProxyV4 registeredProxy = null;
                if (ProxyV4List.TryGetValue(endpointKey, out registeredProxy))
                {
                    if (caller.Equals(registeredProxy))
                        ProxyV4List.Remove(endpointKey);
                }
            }
        }

        public static bool HandlePacketV4(SocketAsyncEventArgs saea, int udpOffset, int udpSize)
        {
            byte[] buffer = saea.Buffer;

            if (udpSize == 31 && buffer[udpOffset + 8] == Constants.UdpEndpoint.ATTACH_TO_CONNECTOR)
            {
                Guid endpointUid = ByteConverter.ToGuid(buffer, udpOffset + 9);
                UdpConnectorV4 connector = null;
                lock (ConnectorV4List)
                {
                    if (ConnectorV4List.Count > 0)
                    {
                        LinkedListNode<UdpConnectorV4> node = ConnectorV4List.First;
                        while (node != null)
                        {
                            if (node.Value.EndpointUid.Equals(endpointUid))
                            {
                                connector = node.Value;
                                break;
                            }
                            node = node.Next;
                        }
                    }
                }

                if (connector != null)
                {
                    connector.AttachEndpoint(saea, udpOffset);

                    buffer[udpOffset + 2] = buffer[udpOffset];
                    buffer[udpOffset + 3] = buffer[udpOffset + 1];
                    buffer[udpOffset] = server_port_higher_byte;
                    buffer[udpOffset + 1] = server_port_lower_byte;

                    buffer[udpOffset + 4] = 0;
                    buffer[udpOffset + 5] = 25;

                    buffer[udpOffset + 6] = 0;
                    buffer[udpOffset + 7] = 0;

                    buffer[udpOffset + 8] = Constants.UdpEndpoint.ATTACHED;

                    IPEndPoint remoteIEP = (IPEndPoint)saea.RemoteEndPoint;
                    byte[] pseudoHeader = UdpPseudoHeader.GetPHv4(remoteIEP.Address);

                    UInt16 checksum = Algorithms.ChecksumUdpV4(pseudoHeader, buffer, udpOffset, 25);
                    Buffer.BlockCopy(ByteConverter.GetBytes(checksum), 0, buffer, udpOffset + 6, 2);

                    saea.SetBuffer(udpOffset, 25);
                    RawUdpBindingV4.Send(saea);

                    return true;
                }
            }
            
            byte[] keyBytes = new byte[] { 0, 0, 0, 0, 0, 0, 0, 0 };
            Buffer.BlockCopy(saea.Buffer, saea.Offset + 12, keyBytes, 2, 4);
            Buffer.BlockCopy(saea.Buffer, udpOffset, keyBytes, 6, 2);
            long endpointKey = ByteConverter.ToInt64(keyBytes, 0);

            UdpProxyV4 udpProxy = null;
            lock (ProxyV4List)
            {
                ProxyV4List.TryGetValue(endpointKey, out udpProxy);
            }

            if (udpProxy != null)
            {
                udpProxy.HandlePacket(saea, udpOffset, udpSize);
                return true;
            }
            
            return false;
        }

        #endregion        
    }
}
