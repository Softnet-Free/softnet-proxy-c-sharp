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
    class RawTcpBindingV6
    {        
        static Socket rawSocket = null;

        public static void Init(IPAddress localIP)
        {
            rawSocket = new Socket(AddressFamily.InterNetworkV6, SocketType.Raw, ProtocolType.Tcp);
            rawSocket.Bind(new IPEndPoint(localIP, 0));

            rawSocket.ReceiveBufferSize = 1048576 * 100;
            rawSocket.SendBufferSize = 1048576 * 100;

            byte[] IN = new byte[4] { 1, 0, 0, 0 };
            byte[] OUT = new byte[4] { 1, 0, 0, 0 };
            rawSocket.IOControl(IOControlCode.ReceiveAll, IN, OUT);
        }

        public static void Start()
        {
            SocketAsyncEventArgs saea = TCPSaeaPool.Get();
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
                    TCPSaeaPool.Add(saea);
                }
            }
            catch (ObjectDisposedException) { } 
        }

        static void OutputCompleted(object sender, SocketAsyncEventArgs saea)
        {
            saea.Completed -= OutputCompleted;
            TCPSaeaPool.Add(saea);
        }

        const int SYN = 0x02;

        static void InputCompleted(object sender, SocketAsyncEventArgs saea)
        {
            try
            {
                if (saea.SocketError == SocketError.Success)
                {
                    int destPort = ByteConverter.ToInt16(saea.Buffer, saea.Offset + 2);

                    if (Constants.BasicProxyPort <= destPort && destPort <= (Constants.BasicProxyPort + 479))
                    {
                        saea.Completed -= InputCompleted;
                        TcpDispatcher.HandleSegmentV6(saea, destPort);

                        SocketAsyncEventArgs saea2 = TCPSaeaPool.Get();
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
                            TaskScheduler.Add(task, 2);
                        }

                        return;
                    }
                    else if (destPort == Constants.TcpRzvPort)
                    {
                        int FLAG_BITS = saea.Buffer[saea.Offset + 13];
                        if ((FLAG_BITS & SYN) != 0)
                        {
                            TcpDispatcher.StoreTCPv6Options(saea);
                        }
                    }
                }

                saea.SetBuffer(saea.Offset, TCPSaeaPool.MTU);
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
                SocketAsyncEventArgs saea = TCPSaeaPool.Get();
                if (saea != null)
                {
                    saea.RemoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                    saea.Completed += InputCompleted;

                    if (rawSocket.ReceiveFromAsync(saea) == false)
                    {
                        InputCompleted(null, saea);
                    }
                }
                else
                {
                    ScheduledTask task = new ScheduledTask(TryReceiveAsync, null);
                    TaskScheduler.Add(task, 2);
                }
            }
            catch (ObjectDisposedException) { }
        }
    }
}
