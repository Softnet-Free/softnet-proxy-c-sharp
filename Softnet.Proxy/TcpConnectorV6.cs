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
using Softnet.Asn;

namespace Softnet.Proxy
{
    class TcpConnectorV6 : ITcpProxyConnector
    {
        enum ConnectorState { INITIAL, AUTH_REQUIRED, SETUP, SHUTDOWN }
        enum ConnectorMode { UNDEFINED, CLIENT_P2P, SERVICE_P2P, CLIENT_PROXY, SERVICE_PROXY }

        ConnectorState m_ConnectorState = ConnectorState.INITIAL;
        ConnectorMode m_ConnectorMode = ConnectorMode.UNDEFINED;

        public readonly long deathTime;

        Guid m_ConnectionGuid;
        MsgSocket m_MsgSocket;
        TcpControl m_TcpControl;
        byte[] m_AuthKey = null;
        
        public byte[] HostIEP;
        

        public TcpConnectorV6(MsgSocket msgSocket)
        {
            m_MsgSocket = msgSocket;
            this.deathTime = SystemClock.Seconds + 30;
        }

        public AddressFamily AddressFamily
        {
            get { return AddressFamily.InterNetworkV6; }
        }

        public TCPOptions GetTCPOptions()
        {
            IEPv6Key endpointKey = new IEPv6Key(HostIEP);
            return TcpDispatcher.FindTCPv6Options(endpointKey);
        }

        public void CreateP2PConnection(byte[] remoteIEP, byte[] secretKey, byte[] remoteSecretKey)
        {
            ASNEncoder asnEncoder = new ASNEncoder();
            SequenceEncoder asnSequence = asnEncoder.Sequence;
            asnSequence.OctetString(remoteIEP);
            asnSequence.OctetString(secretKey);
            asnSequence.OctetString(remoteSecretKey);

            m_MsgSocket.Send(MsgBuilder.Create(Constants.TcpConnector.CREATE_P2P_CONNECTION, asnEncoder));
        }

        public void CreateProxyConnection(int serverPort)
        {
            ASNEncoder asnEncoder = new ASNEncoder();
            SequenceEncoder asnSequence = asnEncoder.Sequence;
            asnSequence.Int32(serverPort);

            m_MsgSocket.Send(MsgBuilder.Create(Constants.TcpConnector.CREATE_PROXY_CONNECTION, asnEncoder));
        }

        public void Shutdown()
        {
            m_ConnectorState = ConnectorState.SHUTDOWN;
            SoftnetMessage message = MsgBuilder.Create(Constants.TcpConnector.ERROR);
            m_MsgSocket.Send(message);
        }

        public void Close()
        {
            m_MsgSocket.Close();
        }

        public void Init(IPEndPoint remoteEndPoint)
        {
            HostIEP = new byte[18];
            Buffer.BlockCopy(remoteEndPoint.Address.GetAddressBytes(), 0, HostIEP, 0, 16);
            Buffer.BlockCopy(ByteConverter.GetBytes((UInt16)remoteEndPoint.Port), 0, HostIEP, 16, 2);

            m_MsgSocket.MinLength = 1;
            m_MsgSocket.MaxLength = 128;
            m_MsgSocket.MessageReceivedHandler = OnMessageReceived;
            m_MsgSocket.InputCompletedHandler = OnInputCompleted;
            m_MsgSocket.NetworkErrorHandler = OnNetworkError;
            m_MsgSocket.FormatErrorHandler = OnFormatError;
            m_MsgSocket.Start();
        }

        void ProcessMessage_ClientP2P(byte[] message)
        {
            SequenceDecoder asnSequence = ASNDecoder.Sequence(message, 1);
            m_ConnectionGuid = ByteConverter.ToGuid(asnSequence.OctetString(16));
            asnSequence.End();

            m_ConnectorState = ConnectorState.AUTH_REQUIRED;
            m_ConnectorMode = ConnectorMode.CLIENT_P2P;

            SendAuthKey();
        }

        void ProcessMessage_ServiceP2P(byte[] message)
        {
            SequenceDecoder asnSequence = ASNDecoder.Sequence(message, 1);
            m_ConnectionGuid = ByteConverter.ToGuid(asnSequence.OctetString(16));
            asnSequence.End();

            m_ConnectorState = ConnectorState.AUTH_REQUIRED;
            m_ConnectorMode = ConnectorMode.SERVICE_P2P;

            SendAuthKey();
        }

        void ProcessMessage_ClientProxy(byte[] message)
        {
            SequenceDecoder asnSequence = ASNDecoder.Sequence(message, 1);
            m_ConnectionGuid = ByteConverter.ToGuid(asnSequence.OctetString(16));
            asnSequence.End();

            m_ConnectorState = ConnectorState.AUTH_REQUIRED;
            m_ConnectorMode = ConnectorMode.CLIENT_PROXY;

            SendAuthKey();
        }

        void ProcessMessage_ServiceProxy(byte[] message)
        {
            SequenceDecoder asnSequence = ASNDecoder.Sequence(message, 1);
            m_ConnectionGuid = ByteConverter.ToGuid(asnSequence.OctetString(16));
            asnSequence.End();

            m_ConnectorState = ConnectorState.AUTH_REQUIRED;
            m_ConnectorMode = ConnectorMode.SERVICE_PROXY;

            SendAuthKey();
        }

        void SendAuthKey()
        {
            m_AuthKey = Randomizer.ByteString(20);

            ASNEncoder asnEncoder = new ASNEncoder();
            SequenceEncoder asnSequence = asnEncoder.Sequence;
            asnSequence.OctetString(m_AuthKey);

            m_MsgSocket.Send(MsgBuilder.Create(Constants.TcpConnector.AUTH_KEY, asnEncoder));
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
                m_TcpControl = TcpDispatcher.GetControl(m_ConnectionGuid);
                m_ConnectorState = ConnectorState.SETUP;

                if (m_ConnectorMode == ConnectorMode.CLIENT_P2P)
                {
                    m_TcpControl.SetupClientConnector(this);
                }
                else if (m_ConnectorMode == ConnectorMode.SERVICE_P2P)
                {
                    m_TcpControl.SetupServiceConnector(this);
                }
                else if (m_ConnectorMode == ConnectorMode.CLIENT_PROXY)
                {
                    m_TcpControl.SetupClientProxyConnector(this);
                }
                else // m_ConnectorMode == ConnectorMode.SERVICE_PROXY
                {
                    m_TcpControl.SetupServiceProxyConnector(this);
                }
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
                    if (messageTag == Constants.TcpConnector.CLIENT_P2P)
                    {
                        ProcessMessage_ClientP2P(message);
                    }
                    else if (messageTag == Constants.TcpConnector.SERVICE_P2P)
                    {
                        ProcessMessage_ServiceP2P(message);
                    }
                    else if (messageTag == Constants.TcpConnector.CLIENT_PROXY)
                    {
                        ProcessMessage_ClientProxy(message);
                    }
                    else if (messageTag == Constants.TcpConnector.SERVICE_PROXY)
                    {
                        ProcessMessage_ServiceProxy(message);
                    }
                    else
                        Terminate();
                }
                else if (m_ConnectorState == ConnectorState.AUTH_REQUIRED)
                {
                    if (message[0] == Constants.TcpConnector.AUTH_HASH)
                    {
                        ProcessMessage_AuthHash(message);
                    }
                    else
                        Terminate();
                }
                else if (m_ConnectorState == ConnectorState.SETUP)
                {
                    if (message[0] == Constants.TcpConnector.P2P_FAILED)
                    {
                        m_TcpControl.OnP2PFailed();
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
        }

        void OnNetworkError() { }

        void OnFormatError() { }

        void Terminate()
        {
            m_MsgSocket.Close();
        }
    }
}
