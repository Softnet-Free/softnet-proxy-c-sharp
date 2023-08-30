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
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.ServiceProcess;
using System.Configuration;
using System.Text;

using System.Net;
using System.Net.Sockets;
using Softnet.ServerKit;

namespace Softnet.Proxy
{
    public partial class ServerRoot : ServiceBase
    {
        public static byte[] SecretKey;

        public static byte[] LocalIPv6Bytes;
        public static byte[] LocalIPv4Bytes;

        public static int Local_TCPv6_MSS = 1440;
        public static int Local_TCPv4_MSS = 1460;
        public static int Local_TCPv6v4_MSS = 1440;

        static bool s_isIPv6Bound;
        static bool s_isIPv4Bound;

        public static SaeaPool CSaeaPool;

        public ServerRoot()
        {
            InitializeComponent();
        }

        protected override void OnStart(string[] args)
        {
            AppLog.WriteLine("Service started!");
            try
            {
                ServerConfig serverConfig = (ServerConfig)ConfigurationManager.GetSection("proxyServer");

                string seckretKey = serverConfig.SecretKey;
                SecretKey = Encoding.BigEndianUnicode.GetBytes(seckretKey);

                IPAddress localIPv6 = serverConfig.IPv6;
                if (localIPv6 != null)
                    LocalIPv6Bytes = localIPv6.GetAddressBytes();

                IPAddress localIPv4 = serverConfig.IPv4;
                if (localIPv4 != null)
                    LocalIPv4Bytes = localIPv4.GetAddressBytes();

                if (!(1220 <= serverConfig.TcpConfig.TCPv6_MSS && serverConfig.TcpConfig.TCPv6_MSS <= 1440))
                    throw new ConfigurationErrorsException("The TCP maximum segment size (MSS) on IPv6 must be in the range [1220 - 1440]");
                Local_TCPv6_MSS = serverConfig.TcpConfig.TCPv6_MSS;

                if (!(536 <= serverConfig.TcpConfig.TCPv4_MSS && serverConfig.TcpConfig.TCPv6_MSS <= 1460))
                    throw new ConfigurationErrorsException("The TCP maximum segment size (MSS) on IPv4 must be in the range [536 - 1460]");
                Local_TCPv4_MSS = serverConfig.TcpConfig.TCPv4_MSS;

                if (Local_TCPv6_MSS < Local_TCPv4_MSS)
                    Local_TCPv6v4_MSS = Local_TCPv6_MSS;
                else
                    Local_TCPv6v4_MSS = Local_TCPv4_MSS;

                if (!(1000 <= serverConfig.TcpConfig.MaxBufferedPackets && serverConfig.TcpConfig.MaxBufferedPackets <= 100000))
                    throw new ConfigurationErrorsException("The pool size for TCP SocketAsyncEventArgs must be in the range [1000 - 100000]");

                if (!(1000 <= serverConfig.UdpConfig.MaxBufferedPackets && serverConfig.UdpConfig.MaxBufferedPackets <= 100000))
                    throw new ConfigurationErrorsException("The pool size for UDP SocketAsyncEventArgs must be in the range [1000 - 100000]");

                if (!(1452 <= serverConfig.UdpConfig.PacketSize && serverConfig.UdpConfig.PacketSize <= 8192))
                    throw new ConfigurationErrorsException("The UDP packet size must be in the range [1452 - 8192]");

                if (!(1000 <= serverConfig.MaxSessions && serverConfig.MaxSessions <= 1000000))
                    throw new ConfigurationErrorsException("The maximum connection establishing sessions must be in the range [1000 - 1000000]");

                TCPSaeaPool.POOL_SIZE = serverConfig.TcpConfig.MaxBufferedPackets;
                TCPSaeaPool.Init();

                UDPSaeaPool.POOL_SIZE = serverConfig.UdpConfig.MaxBufferedPackets;
                UDPSaeaPool.PKT_SIZE = serverConfig.UdpConfig.PacketSize + 48;
                UDPSaeaPool.Init();

                CSaeaPool = new SaeaPool();
                CSaeaPool.Init(serverConfig.MaxSessions, 128);

                Softnet.ServerKit.TaskScheduler.Start();
                Softnet.ServerKit.Monitor.Start(10);

                TcpDispatcher.Init();
                UdpDispatcher.Init();

                if (localIPv6 != null)
                {
                    RawTcpBindingV6.Init(localIPv6);
                    RawTcpBindingV6.Start();
                    TcpListenerV6.Start();

                    RawUdpBindingV6.Init(localIPv6);
                    RawUdpBindingV6.Start();
                    UdpListenerV6.Start();

                    s_isIPv6Bound = true;
                }
                else
                    s_isIPv6Bound = false;

                if (localIPv4 != null)
                {
                    RawTcpBindingV4.Init(localIPv4);
                    RawTcpBindingV4.Start();
                    TcpListenerV4.Start();

                    RawUdpBindingV4.Init(localIPv4);
                    RawUdpBindingV4.Start();
                    UdpListenerV4.Start();

                    s_isIPv4Bound = true;
                }
                else
                    s_isIPv4Bound = false;
            }
            catch (ConfigurationErrorsException e)
            {
                AppLog.WriteLine(e.Message);
                throw e;
            }
            catch (SocketException e)
            {
                AppLog.WriteLine(e.Message);
                throw e;
            }
        }

        protected override void OnStop()
        {
            if (s_isIPv6Bound)
            {
                RawTcpBindingV6.Stop();
                TcpListenerV6.Stop();

                RawUdpBindingV6.Stop();
                UdpListenerV6.Stop();
            }

            if (s_isIPv4Bound)
            {
                RawTcpBindingV4.Stop();
                TcpListenerV4.Stop();

                RawUdpBindingV4.Stop();
                UdpListenerV4.Stop();
            }

            TcpDispatcher.Clear();
            UdpDispatcher.Clear();
            TaskScheduler.Close();

            AppLog.WriteLine("Service stopped!");
        }
    }
}
