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
    static class RawUdpBindingV4
    {
        static Socket rawSocket = null;

        public static void Init(IPAddress localIP)
        {
            rawSocket = new Socket(AddressFamily.InterNetwork, SocketType.Raw, ProtocolType.Udp);
            rawSocket.Bind(new IPEndPoint(localIP, 0));

            rawSocket.ReceiveBufferSize = 1048576 * 100;
            rawSocket.SendBufferSize = 1048576 * 100;

            rawSocket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.HeaderIncluded, false);
            byte[] IN = new byte[4] { 1, 0, 0, 0 };
            byte[] OUT = new byte[4] { 1, 0, 0, 0 };
            rawSocket.IOControl(IOControlCode.ReceiveAll, IN, OUT);
        }

        public static void Start()
        {
            SocketAsyncEventArgs saea = UDPSaeaPool.Get();
            saea.RemoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
            saea.Completed += InputCompleted;

            if (rawSocket.ReceiveFromAsync(saea) == false)
                InputCompleted(null, saea);
        }

        public static void Stop()
        {
            if (rawSocket != null)
                rawSocket.Close();
        }

        public static void Send(SocketAsyncEventArgs saea)
        {
            try
            {
                saea.Completed += OutputCompleted;
                if (rawSocket.SendToAsync(saea) == false)
                {
                    saea.Completed -= OutputCompleted;
                    UDPSaeaPool.Add(saea);
                }
            }
            catch (ObjectDisposedException) { }
        }

        static void OutputCompleted(object sender, SocketAsyncEventArgs saea)
        {
            saea.Completed -= OutputCompleted;
            UDPSaeaPool.Add(saea);
        }

        const int IHL_Mask = 0x0F;

        static void InputCompleted(object sender, SocketAsyncEventArgs saea)
        {
            try
            {
                if (saea.SocketError == SocketError.Success)
                {
                    byte protocol = saea.Buffer[saea.Offset + 9];
                    if (protocol == 17)
                    {
                        int ip_header_length = (saea.Buffer[saea.Offset] & IHL_Mask) * 4;
                        int next_header_offset = saea.Offset + ip_header_length;

                        int destPort = ByteConverter.ToUInt16(saea.Buffer, next_header_offset + 2);
                        if (destPort == Constants.UdpRzvPort)
                        {
                            saea.Completed -= InputCompleted;
                            int udpSize = saea.BytesTransferred - ip_header_length;
                            if (UdpDispatcher.HandlePacketV4(saea, next_header_offset, udpSize))
                            {
                                SocketAsyncEventArgs saea2 = UDPSaeaPool.Get();
                                if (saea2 != null)
                                {
                                    saea2.RemoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                                    saea2.Completed += InputCompleted;

                                    if (rawSocket.ReceiveFromAsync(saea2) == false)
                                        InputCompleted(null, saea2);
                                }
                                else
                                {
                                    ScheduledTask task = new ScheduledTask(TryReceiveAsync, null);
                                    Softnet.ServerKit.TaskScheduler.Add(task, 2);
                                }

                                return;
                            }

                            saea.Completed += InputCompleted;
                        }
                    }
                }

                saea.SetBuffer(saea.Offset, UDPSaeaPool.PKT_SIZE);
                if (rawSocket.ReceiveFromAsync(saea) == false)
                {
                    InputCompleted(sender, saea);
                }
            }
            catch (ObjectDisposedException) { }
        }

        static void TryReceiveAsync(object noData)
        {
            try
            {
                SocketAsyncEventArgs saea = UDPSaeaPool.Get();
                if (saea != null)
                {
                    saea.RemoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                    saea.Completed += InputCompleted;

                    if (rawSocket.ReceiveFromAsync(saea) == false)
                        InputCompleted(null, saea);
                }
                else
                {
                    ScheduledTask task = new ScheduledTask(TryReceiveAsync, null);
                    Softnet.ServerKit.TaskScheduler.Add(task, 2);
                }
            }
            catch (ObjectDisposedException) { }
        }
    }
}
