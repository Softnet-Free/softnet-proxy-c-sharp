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

using Softnet.ServerKit;

namespace Softnet.Proxy
{
    class TcpControl
    {
        enum ControlState { INITIAL, HANDSHAKE, COMPLETED }
        enum HandshakeMode { P2P, PROXY }

        public readonly Guid connectionUid;
        public readonly long deathTime;

        ControlState m_ControlState = ControlState.INITIAL;
        HandshakeMode m_HandshakeMode = HandshakeMode.P2P;

        TcpConnectorV6 m_ServiceConnectorV6 = null;
        TcpConnectorV6 m_ClientConnectorV6 = null;
        TcpConnectorV4 m_ServiceConnectorV4 = null;
        TcpConnectorV4 m_ClientConnectorV4 = null;

        ITcpProxyConnector m_ServiceProxyConnector = null;
        ITcpProxyConnector m_ClientProxyConnector = null;

        VirtualTCPOptions m_ServiceVrtTCPOptions = null;
        VirtualTCPOptions m_ClientVrtTCPOptions = null;

        ITcpProxy m_ServiceProxy = null;
        bool m_ServiceProxyReady = false;

        ITcpProxy m_ClientProxy = null;
        bool m_ClientProxyReady = false;

        object mutex = new object();

        public TcpControl(Guid connectionUid)
        {
            this.connectionUid = connectionUid;
            this.deathTime = SystemClock.Seconds + 30;
        }

        public void SetupClientConnector(TcpConnectorV4 connector)
        {
            lock (mutex)
            {
                if (m_ControlState != ControlState.INITIAL)
                {
                    connector.Close();
                    return;
                }

                m_ClientConnectorV4 = connector;

                if (m_HandshakeMode == HandshakeMode.P2P)
                {
                    if (m_ServiceConnectorV4 != null)
                    {
                        m_ServiceProxyConnector = m_ServiceConnectorV4;
                        m_ClientProxyConnector = m_ClientConnectorV4;
                    }
                    else if (m_ServiceConnectorV6 != null)
                    {
                        m_ServiceProxyConnector = m_ServiceConnectorV6;
                        m_ClientProxyConnector = m_ClientConnectorV4;

                        m_HandshakeMode = HandshakeMode.PROXY;
                    }
                    else
                        return;
                }
                else
                {
                    m_ClientProxyConnector = m_ClientConnectorV4;
                    if (m_ServiceProxyConnector == null)
                        return;
                }

                m_ControlState = ControlState.HANDSHAKE;
            }

            if (m_HandshakeMode == HandshakeMode.P2P)
            {
                CreateP2PConnectionV4();
            }
            else
            {
                CreateProxyConnection();
            }
        }

        public void SetupServiceConnector(TcpConnectorV4 connector)
        {
            lock (mutex)
            {
                if (m_ControlState != ControlState.INITIAL)
                {
                    connector.Close();
                    return;
                }

                m_ServiceConnectorV4 = connector;

                if (m_HandshakeMode == HandshakeMode.P2P)
                {
                    if (m_ClientConnectorV4 != null)
                    {
                        m_ServiceProxyConnector = m_ServiceConnectorV4;
                        m_ClientProxyConnector = m_ClientConnectorV4;
                    }
                    else if (m_ClientConnectorV6 != null)
                    {
                        m_ServiceProxyConnector = m_ServiceConnectorV4;
                        m_ClientProxyConnector = m_ClientConnectorV6;

                        m_HandshakeMode = HandshakeMode.PROXY;
                    }
                    else
                        return;
                }
                else
                {
                    m_ServiceProxyConnector = m_ServiceConnectorV4;
                    if (m_ClientProxyConnector == null)
                        return;
                }

                m_ControlState = ControlState.HANDSHAKE;
            }

            if (m_HandshakeMode == HandshakeMode.P2P)
            {
                CreateP2PConnectionV4();
            }
            else
            {
                CreateProxyConnection();
            }
        }

        public void SetupClientConnector(TcpConnectorV6 connector)
        {
            lock (mutex)
            {
                if (m_ControlState != ControlState.INITIAL)
                {
                    connector.Close();
                    return;
                }

                m_ClientConnectorV6 = connector;

                if (m_HandshakeMode == HandshakeMode.P2P)
                {
                    if (m_ServiceConnectorV6 != null)
                    {
                        m_ServiceProxyConnector = m_ServiceConnectorV6;
                        m_ClientProxyConnector = m_ClientConnectorV6;
                    }
                    else if (m_ServiceConnectorV4 != null)
                    {
                        m_ServiceProxyConnector = m_ServiceConnectorV4;
                        m_ClientProxyConnector = m_ClientConnectorV6;

                        m_HandshakeMode = HandshakeMode.PROXY;
                    }
                    else
                        return;
                }
                else
                {
                    m_ClientProxyConnector = m_ClientConnectorV6;
                    if (m_ServiceProxyConnector == null)
                        return;
                }

                m_ControlState = ControlState.HANDSHAKE;
            }

            if (m_HandshakeMode == HandshakeMode.P2P)
            {
                CreateP2PConnectionV6();
            }
            else
            {
                CreateProxyConnection();
            }
        }

        public void SetupServiceConnector(TcpConnectorV6 connector)
        {
            lock (mutex)
            {
                if (m_ControlState != ControlState.INITIAL)
                {
                    connector.Close();
                    return;
                }

                m_ServiceConnectorV6 = connector;

                if (m_HandshakeMode == HandshakeMode.P2P)
                {
                    if (m_ClientConnectorV6 != null)
                    {
                        m_ServiceProxyConnector = m_ServiceConnectorV6;
                        m_ClientProxyConnector = m_ClientConnectorV6;
                    }
                    else if (m_ClientConnectorV4 != null)
                    {
                        m_ServiceProxyConnector = m_ServiceConnectorV6;
                        m_ClientProxyConnector = m_ClientConnectorV4;

                        m_HandshakeMode = HandshakeMode.PROXY;
                    }
                    else
                        return;
                }
                else
                {
                    m_ServiceProxyConnector = m_ServiceConnectorV6;
                    if (m_ClientProxyConnector == null)
                        return;
                }

                m_ControlState = ControlState.HANDSHAKE;
            }

            if (m_HandshakeMode == HandshakeMode.P2P)
            {
                CreateP2PConnectionV6();
            }
            else
            {
                CreateProxyConnection();
            }
        }

        public void SetupClientProxyConnector(TcpConnectorV4 connector)
        {
            lock (mutex)
            {
                if (m_ControlState != ControlState.INITIAL)
                {
                    connector.Close();
                    return;
                }

                m_HandshakeMode = HandshakeMode.PROXY;

                m_ClientConnectorV4 = connector;
                m_ClientProxyConnector = connector;
                
                if (m_ServiceProxyConnector == null)
                {
                    if (m_ServiceConnectorV4 != null)
                    {
                        m_ServiceProxyConnector = m_ServiceConnectorV4;
                    }
                    else if (m_ServiceConnectorV6 != null)
                    {
                        m_ServiceProxyConnector = m_ServiceConnectorV6;
                    }
                    else
                        return;
                }

                m_ControlState = ControlState.HANDSHAKE;
            }

            CreateProxyConnection();            
        }

        public void SetupServiceProxyConnector(TcpConnectorV4 connector)
        {
            lock (mutex)
            {
                if (m_ControlState != ControlState.INITIAL)
                {
                    connector.Close();
                    return;
                }

                m_HandshakeMode = HandshakeMode.PROXY;

                m_ServiceConnectorV4 = connector;
                m_ServiceProxyConnector = connector;

                if (m_ClientProxyConnector == null)
                {
                    if (m_ClientConnectorV4 != null)
                    {
                        m_ClientProxyConnector = m_ClientConnectorV4;
                    }
                    else if (m_ClientConnectorV6 != null)
                    {
                        m_ClientProxyConnector = m_ClientConnectorV6;
                    }
                    else
                        return;
                }

                m_ControlState = ControlState.HANDSHAKE;
            }

            CreateProxyConnection();
        }

        public void SetupClientProxyConnector(TcpConnectorV6 connector)
        {
            lock (mutex)
            {
                if (m_ControlState != ControlState.INITIAL)
                {
                    connector.Close();
                    return;
                }

                m_HandshakeMode = HandshakeMode.PROXY;

                m_ClientConnectorV6 = connector;
                m_ClientProxyConnector = connector;

                if (m_ServiceProxyConnector == null)
                {
                    if (m_ServiceConnectorV6 != null)
                    {
                        m_ServiceProxyConnector = m_ServiceConnectorV6;
                    }
                    else if (m_ServiceConnectorV4 != null)
                    {
                        m_ServiceProxyConnector = m_ServiceConnectorV4;
                    }
                    else
                        return;
                }

                m_ControlState = ControlState.HANDSHAKE;
            }

            CreateProxyConnection();
        }

        public void SetupServiceProxyConnector(TcpConnectorV6 connector)
        {
            lock (mutex)
            {
                if (m_ControlState != ControlState.INITIAL)
                {
                    connector.Close();
                    return;
                }

                m_HandshakeMode = HandshakeMode.PROXY;

                m_ServiceConnectorV6 = connector;
                m_ServiceProxyConnector = connector;

                if (m_ClientProxyConnector == null)
                {
                    if (m_ClientConnectorV6 != null)
                    {
                        m_ClientProxyConnector = m_ClientConnectorV6;
                    }
                    else if (m_ClientConnectorV4 != null)
                    {
                        m_ClientProxyConnector = m_ClientConnectorV4;
                    }
                    else
                        return;
                }

                m_ControlState = ControlState.HANDSHAKE;
            }

            CreateProxyConnection();
        }

        public void OnP2PFailed()
        {
            lock (mutex)
            {
                if (m_ControlState != ControlState.HANDSHAKE || m_HandshakeMode == HandshakeMode.PROXY)
                    return;
                m_HandshakeMode = HandshakeMode.PROXY;
            }

            CreateProxyConnection();
        }

        void CreateP2PConnectionV6()
        {
            byte[] serviceSecretKey = Randomizer.ByteString(4);
            byte[] clientSecretKey = Randomizer.ByteString(4);

            m_ServiceConnectorV6.CreateP2PConnection(m_ClientConnectorV6.HostIEP, serviceSecretKey, clientSecretKey);
            m_ClientConnectorV6.CreateP2PConnection(m_ServiceConnectorV6.HostIEP, clientSecretKey, serviceSecretKey);
        }

        void CreateP2PConnectionV4()
        {
            byte[] serviceSecretKey = Randomizer.ByteString(4);
            byte[] clientSecretKey = Randomizer.ByteString(4);

            if (m_ServiceConnectorV4.PrivateIEP != null && m_ClientConnectorV4.PrivateIEP != null)
            {
                m_ServiceConnectorV4.CreateP2PConnection(m_ClientConnectorV4.PublicIEP, serviceSecretKey, clientSecretKey, m_ClientConnectorV4.PrivateIEP);
                m_ClientConnectorV4.CreateP2PConnection(m_ServiceConnectorV4.PublicIEP, clientSecretKey, serviceSecretKey, m_ServiceConnectorV4.PrivateIEP);
            }
            else
            {
                m_ServiceConnectorV4.CreateP2PConnection(m_ClientConnectorV4.PublicIEP, serviceSecretKey, clientSecretKey, null);
                m_ClientConnectorV4.CreateP2PConnection(m_ServiceConnectorV4.PublicIEP, clientSecretKey, serviceSecretKey, null);
            }
        }

        void CreateProxyConnection()
        {
            TCPOptions serviceConnectorTCPOptions = m_ServiceProxyConnector.GetTCPOptions();
            if (serviceConnectorTCPOptions == null)
            {
                m_ServiceProxyConnector.Close();
                m_ClientProxyConnector.Close();
                return;
            }

            TCPOptions clientConnectorTCPOptions = m_ClientProxyConnector.GetTCPOptions();
            if (clientConnectorTCPOptions == null)
            {
                m_ServiceProxyConnector.Close();
                m_ClientProxyConnector.Close();
                return;
            }

            VirtualTCPOptions[] VrtTCPOptionsTable = TcpDispatcher.VrtTCPOptionsTable;

            int serviceMSS, clientMSS;

            if (m_ServiceProxyConnector.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            {
                if (m_ClientProxyConnector.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
                {
                    serviceMSS = serviceConnectorTCPOptions.MSS <= WindowsBackgroundService.Local_TCPv6_MSS ? serviceConnectorTCPOptions.MSS : WindowsBackgroundService.Local_TCPv6_MSS;
                    clientMSS = clientConnectorTCPOptions.MSS <= WindowsBackgroundService.Local_TCPv6_MSS ? clientConnectorTCPOptions.MSS : WindowsBackgroundService.Local_TCPv6_MSS;
                }
                else
                {
                    serviceMSS = serviceConnectorTCPOptions.MSS <= WindowsBackgroundService.Local_TCPv6v4_MSS ? serviceConnectorTCPOptions.MSS : WindowsBackgroundService.Local_TCPv6v4_MSS;
                    clientMSS = clientConnectorTCPOptions.MSS <= WindowsBackgroundService.Local_TCPv6v4_MSS ? clientConnectorTCPOptions.MSS : WindowsBackgroundService.Local_TCPv6v4_MSS;
                }
            }
            else
            {
                if (m_ClientProxyConnector.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    serviceMSS = serviceConnectorTCPOptions.MSS <= WindowsBackgroundService.Local_TCPv4_MSS ? serviceConnectorTCPOptions.MSS : WindowsBackgroundService.Local_TCPv4_MSS;
                    clientMSS = clientConnectorTCPOptions.MSS <= WindowsBackgroundService.Local_TCPv4_MSS ? clientConnectorTCPOptions.MSS : WindowsBackgroundService.Local_TCPv4_MSS;
                }
                else
                {
                    serviceMSS = serviceConnectorTCPOptions.MSS <= WindowsBackgroundService.Local_TCPv6v4_MSS ? serviceConnectorTCPOptions.MSS : WindowsBackgroundService.Local_TCPv6v4_MSS;
                    clientMSS = clientConnectorTCPOptions.MSS <= WindowsBackgroundService.Local_TCPv6v4_MSS ? clientConnectorTCPOptions.MSS : WindowsBackgroundService.Local_TCPv6v4_MSS;
                }
            }

            if (serviceConnectorTCPOptions.WindowScaleSupported && clientConnectorTCPOptions.WindowScaleSupported)
            {
                int serviceWindowScale = serviceConnectorTCPOptions.WindowScale;
                int clientWindowScale = clientConnectorTCPOptions.WindowScale;

                if (serviceConnectorTCPOptions.SACKPermitted && clientConnectorTCPOptions.SACKPermitted)
                {
                    for (int index = 0; index <= 210; index += 15)
                    {
                        if (VrtTCPOptionsTable[index].MSS <= serviceMSS)
                        {
                            m_ClientVrtTCPOptions = VrtTCPOptionsTable[index + serviceWindowScale];
                            break;
                        }
                    }

                    if (m_ClientVrtTCPOptions == null)
                    {
                        m_ServiceProxyConnector.Close();
                        m_ClientProxyConnector.Close();
                        return;
                    }

                    for (int index = 0; index <= 210; index += 15)
                    {
                        if (VrtTCPOptionsTable[index].MSS <= clientMSS)
                        {
                            m_ServiceVrtTCPOptions = VrtTCPOptionsTable[index + clientWindowScale];
                            break;
                        }
                    }

                    if (m_ServiceVrtTCPOptions == null)
                    {
                        m_ServiceProxyConnector.Close();
                        m_ClientProxyConnector.Close();
                        return;
                    }
                }
                else
                {
                    for (int index = 225; index <= 435; index += 15)
                    {
                        if (VrtTCPOptionsTable[index].MSS <= serviceMSS)
                        {
                            m_ClientVrtTCPOptions = VrtTCPOptionsTable[index + serviceWindowScale];
                            break;
                        }
                    }

                    if (m_ClientVrtTCPOptions == null)
                    {
                        m_ServiceProxyConnector.Close();
                        m_ClientProxyConnector.Close();
                        return;
                    }

                    for (int index = 225; index <= 435; index += 15)
                    {
                        if (VrtTCPOptionsTable[index].MSS <= clientMSS)
                        {
                            m_ServiceVrtTCPOptions = VrtTCPOptionsTable[index + clientWindowScale];
                            break;
                        }
                    }

                    if (m_ServiceVrtTCPOptions == null)
                    {
                        m_ServiceProxyConnector.Close();
                        m_ClientProxyConnector.Close();
                        return;
                    }
                }
            }
            else
            {
                if (serviceConnectorTCPOptions.SACKPermitted && clientConnectorTCPOptions.SACKPermitted)
                {
                    for (int index = 450; index <= 464; index++)
                    {
                        if (VrtTCPOptionsTable[index].MSS <= serviceMSS)
                        {
                            m_ClientVrtTCPOptions = VrtTCPOptionsTable[index];
                            break;
                        }
                    }

                    if (m_ClientVrtTCPOptions == null)
                    {
                        m_ServiceProxyConnector.Close();
                        m_ClientProxyConnector.Close();
                        return;
                    }

                    for (int index = 450; index <= 464; index++)
                    {
                        if (VrtTCPOptionsTable[index].MSS <= clientMSS)
                        {
                            m_ServiceVrtTCPOptions = VrtTCPOptionsTable[index];
                            break;
                        }
                    }

                    if (m_ServiceVrtTCPOptions == null)
                    {
                        m_ServiceProxyConnector.Close();
                        m_ClientProxyConnector.Close();
                        return;
                    }
                }
                else
                {
                    for (int index = 465; index <= 479; index++)
                    {
                        if (VrtTCPOptionsTable[index].MSS <= serviceMSS)
                        {
                            m_ClientVrtTCPOptions = VrtTCPOptionsTable[index];
                            break;
                        }
                    }

                    if (m_ClientVrtTCPOptions == null)
                    {
                        m_ServiceProxyConnector.Close();
                        m_ClientProxyConnector.Close();
                        return;
                    }

                    for (int index = 465; index <= 479; index++)
                    {
                        if (VrtTCPOptionsTable[index].MSS <= clientMSS)
                        {
                            m_ServiceVrtTCPOptions = VrtTCPOptionsTable[index];
                            break;
                        }
                    }

                    if (m_ServiceVrtTCPOptions == null)
                    {
                        m_ServiceProxyConnector.Close();
                        m_ClientProxyConnector.Close();
                        return;
                    }                
                }
            }

            m_ServiceProxyConnector.CreateProxyConnection(m_ServiceVrtTCPOptions.ServerPort);
            m_ClientProxyConnector.CreateProxyConnection(m_ClientVrtTCPOptions.ServerPort);
        }
    
        public void SetServiceProxy(ITcpProxy proxy)
        {
            lock (mutex)
            {
                if (m_ServiceProxy != null)
                    return;
                m_ServiceProxy = proxy;

                if (m_ClientProxy == null)
                    return;
            }

            AcceptProxyEndpoints();
        }

        public void SetClientProxy(ITcpProxy proxy)
        {
            lock (mutex)
            {
                if (m_ClientProxy != null)
                    return;
                m_ClientProxy = proxy;

                if (m_ServiceProxy == null)
                    return;
            }

            AcceptProxyEndpoints();
        }

        void AcceptProxyEndpoints()
        {
            if (m_ServiceProxy.VrtTCPOptions.ServerPort != m_ServiceVrtTCPOptions.ServerPort)
            {
                m_ServiceProxyConnector.Close();
                m_ClientProxyConnector.Close();
                return;
            }

            if (m_ClientProxy.VrtTCPOptions.ServerPort != m_ClientVrtTCPOptions.ServerPort)
            {
                m_ServiceProxyConnector.Close();
                m_ClientProxyConnector.Close();
                return;
            }

            TCPOptions serviceHostTCPOptions = m_ServiceProxy.HostTCPOptions;
            TCPOptions clientHostTCPOptions = m_ClientProxy.HostTCPOptions;

            if (serviceHostTCPOptions.MSS < m_ClientVrtTCPOptions.MSS)
            {
                m_ServiceProxyConnector.Close();
                m_ClientProxyConnector.Close();
                return;
            }

            if (clientHostTCPOptions.MSS < m_ServiceVrtTCPOptions.MSS)
            {
                m_ServiceProxyConnector.Close();
                m_ClientProxyConnector.Close();
                return;
            }

            if (m_ClientVrtTCPOptions.SACKPermitted) // m_ServiceVrtTCPOptions.SACKPermitted is also true
            {
                if (serviceHostTCPOptions.SACKPermitted == false)
                {
                    m_ServiceProxyConnector.Close();
                    m_ClientProxyConnector.Close();
                    return;
                }

                if (clientHostTCPOptions.SACKPermitted == false)
                {
                    m_ServiceProxyConnector.Close();
                    m_ClientProxyConnector.Close();
                    return;
                }
            }

            if (m_ClientVrtTCPOptions.WindowScaleSupported) // m_ServiceVrtTCPOptions.WindowScaleSupported is also true
            {
                if (serviceHostTCPOptions.WindowScaleSupported == false)
                {
                    m_ServiceProxyConnector.Close();
                    m_ClientProxyConnector.Close();
                    return;
                }

                if (clientHostTCPOptions.WindowScaleSupported == false)
                {
                    m_ServiceProxyConnector.Close();
                    m_ClientProxyConnector.Close();
                    return;
                }

                if (serviceHostTCPOptions.WindowScale != m_ClientVrtTCPOptions.WindowScale)
                {
                    m_ClientProxy.AdjustWindowScale(serviceHostTCPOptions.WindowScale);
                }

                if (clientHostTCPOptions.WindowScale != m_ServiceVrtTCPOptions.WindowScale)
                {
                    m_ServiceProxy.AdjustWindowScale(clientHostTCPOptions.WindowScale);
                }
            }

            m_ServiceProxy.OnAccepted();
            m_ClientProxy.OnAccepted();
        }

        public void OnServiceProxyReady()
        {
            lock (mutex)
            {
                m_ServiceProxyReady = true;
                if (m_ClientProxyReady == false)
                    return;
            }

            m_ServiceProxy.SetPeerProxy(m_ClientProxy);
            m_ClientProxy.SetPeerProxy(m_ServiceProxy);
        }

        public void OnClientProxyReady()
        {
            lock (mutex)
            {
                m_ClientProxyReady = true;
                if (m_ServiceProxyReady == false)
                    return;
            }

            m_ServiceProxy.SetPeerProxy(m_ClientProxy);
            m_ClientProxy.SetPeerProxy(m_ServiceProxy);
        }
    }
}
