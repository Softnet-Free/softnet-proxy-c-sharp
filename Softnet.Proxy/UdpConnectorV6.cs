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

        public void Shutdown(int errorCode)
        {
            m_ConnectorState = ConnectorState.COMPLETED;
            SoftnetMessage message = MsgBuilder.CreateErrorMessage(Constants.UdpConnector.ERROR, errorCode);
            m_MsgSocket.Send(message);
        }

        public void Close()
        {
            m_MsgSocket.Close();
        }

        public void AttachEndpoint(SocketAsyncEventArgs saea)
        {
            if (m_UdpEndpointAttached)
                return;

            m_HostIEP = (IPEndPoint)saea.RemoteEndPoint;
            byte[] hostIep = new byte[18];
            Buffer.BlockCopy(m_HostIEP.Address.GetAddressBytes(), 0, hostIep, 0, 16);
            Buffer.BlockCopy(saea.Buffer, saea.Offset, hostIep, 16, 2);

            lock (mutex)
            {
                if (m_UdpEndpointAttached)
                    return;
                m_UdpEndpointAttached = true;

                HostIEP = hostIep;

                if (m_ConnectorState == ConnectorState.AUTHENTICATED)
                    m_ConnectorState = ConnectorState.SETUP;
                else
                    return;
            }

            if (m_ConnectorMode == ConnectorMode.CLIENT)
            {
                m_UdpControl.SetupClientConnector(this);
            }
            else // m_ConnectorMode == ConnectorMode.SERVICE
            {
                m_UdpControl.SetupServiceConnector(this);
            }
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
            UdpDispatcher.RegisterProxyV6(endpointKey, udpProxy);
            Softnet.ServerKit.Monitor.Add(udpProxy);
            return udpProxy;
        }

        public void OnProxyEstablished()
        {
            SendProxyEstablishingNotification(1);
        }

        void SendProxyEstablishingNotification(object state)
        {
            SocketAsyncEventArgs saea = UDPSaeaPool.Get();
            if (saea == null)
                return;

            byte[] buffer = saea.Buffer;
            int offset = saea.Offset;

            buffer[offset] = UdpDispatcher.server_port_higher_byte;
            buffer[offset + 1] = UdpDispatcher.server_port_lower_byte;
            buffer[offset + 2] = HostIEP[16];
            buffer[offset + 3] = HostIEP[17];

            buffer[offset + 4] = 0;
            buffer[offset + 5] = 25;

            buffer[offset + 6] = 0;
            buffer[offset + 7] = 0;

            buffer[offset + 8] = Constants.UdpEndpoint.PROXY_ESTABLISHED;
            Buffer.BlockCopy(ByteConverter.GetBytes(EndpointUid), 0, buffer, offset + 9, 16);

            byte[] pseudoHeader = UdpPseudoHeader.GetPHv6(m_HostIEP.Address);
            UInt16 checksum = Algorithms.ChecksumUdpV6(pseudoHeader, buffer, offset, 25);
            Buffer.BlockCopy(ByteConverter.GetBytes(checksum), 0, buffer, offset + 6, 2);

            saea.SetBuffer(offset, 25);
            saea.RemoteEndPoint = m_HostIEP;
            RawUdpBindingV6.Send(saea);

            int waitSeconds = (int)state;
            if (waitSeconds <= 8)
            {
                ScheduledTask task = new ScheduledTask(SendProxyEstablishingNotification, this, waitSeconds * 2);
                TaskScheduler.Add(task, waitSeconds);
            }
        }

        void ProcessMessage_Client(byte[] message)
        {
            SequenceDecoder asnSequence = ASNDecoder.Sequence(message, 1);
            m_ConnectionUid = ByteConverter.ToGuid(asnSequence.OctetString(16));
            asnSequence.End();

            m_ConnectorState = ConnectorState.AUTH_REQUIRED;
            m_ConnectorMode = ConnectorMode.CLIENT;

            SendAuthKey();
        }

        void ProcessMessage_Service(byte[] message)
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
                {
                    m_UdpControl.SetupClientConnector(this);
                }
                else // m_ConnectorMode == ConnectorMode.SERVICE
                {
                    m_UdpControl.SetupServiceConnector(this);
                }
            }
            else
            {
                Shutdown(ErrorCodes.AUTH_FAILED);
            }
        }

        void OnMessageReceived(byte[] message)
        {
            try
            {
                byte messageTag = message[0];
                if (m_ConnectorState == ConnectorState.INITIAL)
                {
                    if (messageTag == Constants.UdpConnector.CLIENT)
                    {
                        ProcessMessage_Client(message);
                    }
                    else if (messageTag == Constants.UdpConnector.SERVICE)
                    {
                        ProcessMessage_Service(message);
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
                        m_PeerConnector.SendMessage(MsgBuilder.Create(message));
                    }
                    else if (messageTag == Constants.UdpConnector.P2P_FAILED)
                    {
                        m_UdpControl.OnP2PFailed();
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
