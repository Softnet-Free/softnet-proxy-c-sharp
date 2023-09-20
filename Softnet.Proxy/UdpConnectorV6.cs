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
using System.Configuration;

using Softnet.ServerKit;
using Softnet.Asn;

namespace Softnet.Proxy
{
    class UdpConnectorV6 : IUdpConnector, STContext
    {
        enum ConnectorState { INITIAL, AUTH_REQUIRED, AUTHENTICATED, SETUP, COMPLETED }
        enum ConnectorMode { UNDEFINED, CLIENT, SERVICE }

        ConnectorState m_ConnectorState = ConnectorState.INITIAL;
        ConnectorMode m_ConnectorMode = ConnectorMode.UNDEFINED;

        object mutex = new object();
        MsgSocket m_MsgSocket = null;
        Guid m_ConnectionUid = Guid.Empty;
        UdpControl m_UdpControl = null;
        byte[] m_AuthKey = null;
        IUdpConnector m_PeerConnector = null;
        bool m_Completed = false;

        bool m_UdpEndpointAttached = false;
        IPEndPoint m_HostIEP = null;

        public byte[] HostIEP = null;

        public readonly Guid EndpointUid;
        public readonly long deathTime;

        public UdpConnectorV6(MsgSocket msgSocket)
        {
            m_MsgSocket = msgSocket;
            this.EndpointUid = Guid.NewGuid();
            this.deathTime = SystemClock.Seconds + 30;
        }

        public bool Completed
        {
            get { return m_Completed; }
        }

        public void Init()
        {
            m_MsgSocket.MinLength = 1;
            m_MsgSocket.MaxLength = 128;
            m_MsgSocket.MessageReceivedHandler = OnMessageReceived;
            m_MsgSocket.InputCompletedHandler = OnInputCompleted;
            m_MsgSocket.NetworkErrorHandler = OnNetworkError;
            m_MsgSocket.FormatErrorHandler = OnFormatError;
            m_MsgSocket.Start();
        }

        public void Shutdown()
        {
            m_ConnectorState = ConnectorState.COMPLETED;
            SoftnetMessage message = MsgBuilder.Create(Constants.UdpConnector.ERROR);
            m_MsgSocket.Send(message);
        }

        public void Close()
        {
            m_MsgSocket.Close();
        }

        public bool AttachEndpoint(SocketAsyncEventArgs saea)
        {
            if (m_UdpEndpointAttached)
                return false;

            m_HostIEP = (IPEndPoint)saea.RemoteEndPoint;
            byte[] hostIep = new byte[18];
            Buffer.BlockCopy(m_HostIEP.Address.GetAddressBytes(), 0, hostIep, 0, 16);
            Buffer.BlockCopy(saea.Buffer, saea.Offset, hostIep, 16, 2);

            lock (mutex)
            {
                if (m_UdpEndpointAttached)
                    return false;
                m_UdpEndpointAttached = true;

                HostIEP = hostIep;

                if (m_ConnectorState == ConnectorState.AUTHENTICATED)
                    m_ConnectorState = ConnectorState.SETUP;
                else
                    return true;
            }

            if (m_ConnectorMode == ConnectorMode.CLIENT)
                m_UdpControl.SetupClientConnector(this);
            else // m_ConnectorMode == ConnectorMode.SERVICE
                m_UdpControl.SetupServiceConnector(this);

            return true;
        }

        public void SetPeerConnector(IUdpConnector peerConnector)
        {
            m_PeerConnector = peerConnector;
        }

        public void SendMessage(SoftnetMessage message)
        {
            m_MsgSocket.Send(message);
        }

        public void CreateP2PConnection(byte[] remoteIEP, Guid remoteEndpointUid)
        {
            ASNEncoder asnEncoder = new ASNEncoder();
            SequenceEncoder asnSequence = asnEncoder.Sequence;
            asnSequence.OctetString(remoteIEP);
            asnSequence.OctetString(ByteConverter.GetBytes(remoteEndpointUid));

            m_MsgSocket.Send(MsgBuilder.Create(Constants.UdpConnector.CREATE_P2P_CONNECTION, asnEncoder));
        }

        public IUdpProxy RegisterProxyEndpoint()
        {
            IEPv6Key endpointKey = new IEPv6Key(HostIEP);
            UdpProxyV6 udpProxy = new UdpProxyV6(endpointKey, m_HostIEP, HostIEP);
            udpProxy.Init();
            UdpDispatcher.RegisterProxyV6(endpointKey, udpProxy);
            Softnet.ServerKit.Monitor.Add(udpProxy);
            return udpProxy;
        }

        public void OnProxyConnectionCreated()
        {
            m_MsgSocket.Send(MsgBuilder.Create(Constants.UdpConnector.PROXY_CONNECTION_CREATED));
        }

        void ProcessMessage_ClientEndpoint(byte[] message)
        {
            SequenceDecoder asnSequence = ASNDecoder.Sequence(message, 1);
            m_ConnectionUid = ByteConverter.ToGuid(asnSequence.OctetString(16));
            asnSequence.End();

            m_ConnectorState = ConnectorState.AUTH_REQUIRED;
            m_ConnectorMode = ConnectorMode.CLIENT;

            SendAuthKey();
        }

        void ProcessMessage_ServiceEndpoint(byte[] message)
        {
            SequenceDecoder asnSequence = ASNDecoder.Sequence(message, 1);
            m_ConnectionUid = ByteConverter.ToGuid(asnSequence.OctetString(16));
            asnSequence.End();

            m_ConnectorState = ConnectorState.AUTH_REQUIRED;
            m_ConnectorMode = ConnectorMode.SERVICE;

            SendAuthKey();
        }

        void SendAuthKey()
        {
            m_AuthKey = Randomizer.ByteString(20);

            ASNEncoder asnEncoder = new ASNEncoder();
            SequenceEncoder asnSequence = asnEncoder.Sequence;
            asnSequence.OctetString(m_AuthKey);
            asnSequence.OctetString(ByteConverter.GetBytes(this.EndpointUid));

            m_MsgSocket.Send(MsgBuilder.Create(Constants.UdpConnector.AUTH_KEY, asnEncoder));
        }

        void ProcessMessage_AuthHash(byte[] message)
        {
            SequenceDecoder asnSequence = ASNDecoder.Sequence(message, 1);
            byte[] authHashReceived = asnSequence.OctetString(20);
            byte[] authKey2 = asnSequence.OctetString(20);
            asnSequence.End();

            byte[] authHash = PasswordHash.Compute(m_AuthKey, authKey2, ServerRoot.SecretKey);

            if (authHashReceived.SequenceEqual(authHash))
            {
                m_UdpControl = UdpDispatcher.GetControl(m_ConnectionUid);

                lock (mutex)
                {
                    if (m_UdpEndpointAttached)
                        m_ConnectorState = ConnectorState.SETUP;
                    else
                    {
                        m_ConnectorState = ConnectorState.AUTHENTICATED;
                        return;
                    }
                }

                if (m_ConnectorMode == ConnectorMode.CLIENT)
                    m_UdpControl.SetupClientConnector(this);
                else // m_ConnectorMode == ConnectorMode.SERVICE
                    m_UdpControl.SetupServiceConnector(this);
            }
            else
            {
                Shutdown();
            }
        }

        void OnMessageReceived(byte[] message)
        {
            try
            {
                byte messageTag = message[0];
                if (m_ConnectorState == ConnectorState.INITIAL)
                {
                    if (messageTag == Constants.UdpConnector.CLIENT_ENDPOINT)
                    {
                        ProcessMessage_ClientEndpoint(message);
                    }
                    else if (messageTag == Constants.UdpConnector.SERVICE_ENDPOINT)
                    {
                        ProcessMessage_ServiceEndpoint(message);
                    }
                    else
                        Terminate();
                }
                else if (m_ConnectorState == ConnectorState.AUTH_REQUIRED)
                {
                    if (messageTag == Constants.UdpConnector.AUTH_HASH)
                    {
                        ProcessMessage_AuthHash(message);
                    }
                    else
                        Terminate();
                }
                else if (m_ConnectorState == ConnectorState.SETUP)
                {
                    if (messageTag == Constants.UdpConnector.P2P_HOLE_PUNCHED)
                    {
                        if (m_ConnectorMode == ConnectorMode.SERVICE)
                            m_PeerConnector.SendMessage(MsgBuilder.Create(message));
                        else
                            Terminate();
                    }
                    else if (messageTag == Constants.UdpConnector.P2P_CONNECTION_CREATED)
                    {
                        if (m_ConnectorMode == ConnectorMode.CLIENT)
                            m_PeerConnector.SendMessage(MsgBuilder.Create(message));
                        else
                            Terminate();
                    }
                    else if (messageTag == Constants.UdpConnector.CREATE_PROXY_CONNECTION)
                    {
                        if (m_ConnectorMode == ConnectorMode.SERVICE)
                            m_UdpControl.CreateProxyConnection();
                        else
                            Terminate();
                    }
                    else
                        Terminate();
                }
            }
            catch (AsnException)
            {
                Terminate();
            }
        }

        void OnInputCompleted()
        {
            m_MsgSocket.Close();
            m_Completed = true;
        }

        void OnNetworkError()
        {
            m_Completed = true;
        }

        void OnFormatError()
        {
            m_Completed = true;
        }

        public void Terminate()
        {
            m_MsgSocket.Close();
            m_Completed = true;
        }
    }
}
