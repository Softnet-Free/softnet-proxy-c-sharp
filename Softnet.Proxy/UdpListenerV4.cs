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
    static class UdpListenerV4
    {
        static Socket s_serverSocket;
        static Thread s_thread;
        static bool s_running = false;

        public static void Start()
        {
            s_serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            s_serverSocket.Bind(new IPEndPoint(IPAddress.Any, Constants.UdpRzvPort));
            s_serverSocket.Listen(100);

            s_running = true;
            s_thread = new Thread(new ThreadStart(ThreadProc));
            s_thread.Start();
        }

        public static void Stop()
        {
            s_running = false;
            if (s_serverSocket != null)
                s_serverSocket.Close();
        }

        static void ThreadProc()
        {
            while (s_running)
            {
                SocketAsyncEventArgs saea = ServerRoot.CSaeaPool.Get();
                if (saea != null)
                {
                    try
                    {
                        Socket socket = s_serverSocket.Accept();

                        var msgSocket = new MsgSocket(socket, saea, ServerRoot.CSaeaPool);
                        var connector = new UdpConnectorV4(msgSocket);
                        UdpDispatcher.RegisterConnector(connector);
                        connector.Init();
                    }
                    catch (SocketException)
                    {
                        ServerRoot.CSaeaPool.Add(saea);
                    }
                    catch (ObjectDisposedException)
                    {
                        ServerRoot.CSaeaPool.Add(saea);
                        return;
                    }
                }
                else
                {
                    Thread.Sleep(1000);
                }
            }
        }
    }
}
