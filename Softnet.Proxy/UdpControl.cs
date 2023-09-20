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
    class UdpControl
    {
        public readonly Guid connectionUid;
        public readonly long deathTime;

        enum ControlState { INITIAL, HANDSHAKE }
        enum HandshakeMode { P2P, PROXY }

        ControlState m_ControlState = ControlState.INITIAL;
        HandshakeMode m_HandshakeMode = HandshakeMode.P2P;

        UdpConnectorV6 m_ClientConnectorV6 = null;
        UdpConnectorV6 m_ServiceConnectorV6 = null;
        UdpConnectorV4 m_ClientConnectorV4 = null;
        UdpConnectorV4 m_ServiceConnectorV4 = null;

        object mutex = new object();

        public UdpControl(Guid connectionUid)
        {
            this.connectionUid = connectionUid;
            this.deathTime = SystemClock.Seconds + 60;
        }

        public void SetupClientConnector(UdpConnectorV6 connector)
        {
            lock (mutex)
            {
                if (m_ControlState != ControlState.INITIAL)
                    return;
                m_ClientConnectorV6 = connector;

                if (m_ServiceConnectorV6 == null)
                {
                    if (m_ServiceConnectorV4 == null)
                        return;

                    m_ClientConnectorV6.SetPeerConnector(m_ServiceConnectorV4);
                    m_ServiceConnectorV4.SetPeerConnector(m_ClientConnectorV6);

                    m_HandshakeMode = HandshakeMode.PROXY;
                }
                else
                {
                    m_ClientConnectorV6.SetPeerConnector(m_ServiceConnectorV6);
                    m_ServiceConnectorV6.SetPeerConnector(m_ClientConnectorV6);

                    m_HandshakeMode = HandshakeMode.P2P;
                }

                m_ControlState = ControlState.HANDSHAKE;
            }

            if (m_HandshakeMode == HandshakeMode.P2P)
            {
                CreateP2PConnectionV6();
            }
            else
            {
                IUdpProxy clientProxy = m_ClientConnectorV6.RegisterProxyEndpoint();
                IUdpProxy serviceProxy = m_ServiceConnectorV4.RegisterProxyEndpoint();

                clientProxy.SetPeerProxy(serviceProxy);
                serviceProxy.SetPeerProxy(clientProxy);

                m_ClientConnectorV6.OnProxyConnectionCreated();
                m_ServiceConnectorV4.OnProxyConnectionCreated();
            }
        }

        public void SetupServiceConnector(UdpConnectorV6 connector)
        {
            lock (mutex)
            {
                if (m_ControlState != ControlState.INITIAL)
                    return;
                m_ServiceConnectorV6 = connector;

                if (m_ClientConnectorV6 == null)
                {
                    if (m_ClientConnectorV4 == null)
                        return;

                    m_ServiceConnectorV6.SetPeerConnector(m_ClientConnectorV4);
                    m_ClientConnectorV4.SetPeerConnector(m_ServiceConnectorV6);

                    m_HandshakeMode = HandshakeMode.PROXY;
                }
                else
                {
                    m_ServiceConnectorV6.SetPeerConnector(m_ClientConnectorV6);
                    m_ClientConnectorV6.SetPeerConnector(m_ServiceConnectorV6);

                    m_HandshakeMode = HandshakeMode.P2P;
                }

                m_ControlState = ControlState.HANDSHAKE;
            }

            if (m_HandshakeMode == HandshakeMode.P2P)
            {
                CreateP2PConnectionV6();
            }
            else
            {
                IUdpProxy clientProxy = m_ClientConnectorV4.RegisterProxyEndpoint();
                IUdpProxy serviceProxy = m_ServiceConnectorV6.RegisterProxyEndpoint();

                clientProxy.SetPeerProxy(serviceProxy);
                serviceProxy.SetPeerProxy(clientProxy);

                m_ClientConnectorV4.OnProxyConnectionCreated();
                m_ServiceConnectorV6.OnProxyConnectionCreated();
            }
        }

        public void SetupClientConnector(UdpConnectorV4 connector)
        {
            lock (mutex)
            {
                if (m_ControlState != ControlState.INITIAL)
                    return;
                m_ClientConnectorV4 = connector;

                if (m_ServiceConnectorV4 == null)
                {
                    if (m_ServiceConnectorV6 == null)
                        return;

                    m_ClientConnectorV4.SetPeerConnector(m_ServiceConnectorV6);
                    m_ServiceConnectorV6.SetPeerConnector(m_ClientConnectorV4);

                    m_HandshakeMode = HandshakeMode.PROXY;
                }
                else
                {
                    m_ClientConnectorV4.SetPeerConnector(m_ServiceConnectorV4);
                    m_ServiceConnectorV4.SetPeerConnector(m_ClientConnectorV4);

                    m_HandshakeMode = HandshakeMode.P2P;
                }

                m_ControlState = ControlState.HANDSHAKE;
            }

            if (m_HandshakeMode == HandshakeMode.P2P)
            {
                CreateP2PConnectionV4();
            }
            else
            {
                IUdpProxy clientProxy = m_ClientConnectorV4.RegisterProxyEndpoint();
                IUdpProxy serviceProxy = m_ServiceConnectorV6.RegisterProxyEndpoint();

                clientProxy.SetPeerProxy(serviceProxy);
                serviceProxy.SetPeerProxy(clientProxy);

                m_ClientConnectorV4.OnProxyConnectionCreated();
                m_ServiceConnectorV6.OnProxyConnectionCreated();
            }
        }

        public void SetupServiceConnector(UdpConnectorV4 connector)
        {
            lock (mutex)
            {
                if (m_ControlState != ControlState.INITIAL)
                    return;
                m_ServiceConnectorV4 = connector;

                if (m_ClientConnectorV4 == null)
                {
                    if (m_ClientConnectorV6 == null)
                        return;

                    m_ServiceConnectorV4.SetPeerConnector(m_ClientConnectorV6);
                    m_ClientConnectorV6.SetPeerConnector(m_ServiceConnectorV4);

                    m_HandshakeMode = HandshakeMode.PROXY;
                }
                else
                {
                    m_ServiceConnectorV4.SetPeerConnector(m_ClientConnectorV4);
                    m_ClientConnectorV4.SetPeerConnector(m_ServiceConnectorV4);

                    m_HandshakeMode = HandshakeMode.P2P;
                }

                m_ControlState = ControlState.HANDSHAKE;
            }

            if (m_HandshakeMode == HandshakeMode.P2P)
            {
                CreateP2PConnectionV4();
            }
            else
            {
                IUdpProxy clientProxy = m_ClientConnectorV6.RegisterProxyEndpoint();
                IUdpProxy serviceProxy = m_ServiceConnectorV4.RegisterProxyEndpoint();

                clientProxy.SetPeerProxy(serviceProxy);
                serviceProxy.SetPeerProxy(clientProxy);

                m_ClientConnectorV6.OnProxyConnectionCreated();
                m_ServiceConnectorV4.OnProxyConnectionCreated();
            }
        }

        void CreateP2PConnectionV6()
        {
            m_ServiceConnectorV6.CreateP2PConnection(m_ClientConnectorV6.HostIEP, m_ClientConnectorV6.EndpointUid);
            m_ClientConnectorV6.CreateP2PConnection(m_ServiceConnectorV6.HostIEP, m_ServiceConnectorV6.EndpointUid);
        }

        void CreateP2PConnectionV4()
        {
            if (m_ServiceConnectorV4.HostPrivateIEP != null && m_ClientConnectorV4.HostPrivateIEP != null)
            {
                m_ServiceConnectorV4.CreateP2PConnection(m_ClientConnectorV4.HostIEP, m_ClientConnectorV4.HostPrivateIEP, m_ClientConnectorV4.EndpointUid);
                m_ClientConnectorV4.CreateP2PConnection(m_ServiceConnectorV4.HostIEP, m_ServiceConnectorV4.HostPrivateIEP, m_ServiceConnectorV4.EndpointUid);
            }
            else
            {
                m_ServiceConnectorV4.CreateP2PConnection(m_ClientConnectorV4.HostIEP, m_ClientConnectorV4.EndpointUid);
                m_ClientConnectorV4.CreateP2PConnection(m_ServiceConnectorV4.HostIEP, m_ServiceConnectorV4.EndpointUid);
            }
        }

        public void CreateProxyConnection()
        {
            lock (mutex)
            {
                if (m_ControlState != ControlState.HANDSHAKE || m_HandshakeMode != HandshakeMode.P2P)
                    return;
                m_HandshakeMode = HandshakeMode.PROXY;
            }

            if (m_ClientConnectorV6 != null)
            {
                IUdpProxy clientProxy = m_ClientConnectorV6.RegisterProxyEndpoint();
                IUdpProxy serviceProxy = m_ServiceConnectorV6.RegisterProxyEndpoint();

                clientProxy.SetPeerProxy(serviceProxy);
                serviceProxy.SetPeerProxy(clientProxy);

                m_ClientConnectorV6.OnProxyConnectionCreated();
                m_ServiceConnectorV6.OnProxyConnectionCreated();
            }
            else
            {
                IUdpProxy clientProxy = m_ClientConnectorV4.RegisterProxyEndpoint();
                IUdpProxy serviceProxy = m_ServiceConnectorV4.RegisterProxyEndpoint();

                clientProxy.SetPeerProxy(serviceProxy);
                serviceProxy.SetPeerProxy(clientProxy);

                m_ClientConnectorV4.OnProxyConnectionCreated();
                m_ServiceConnectorV4.OnProxyConnectionCreated();            
            }
        }
    }
}
