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
    class TcpProxyV6 : ITcpProxy, Monitorable
    {
        IEPv6Key m_EndpointKey;

        enum ConnectionState
        {
            WAITING_SYN,
            WAITING_HEADER,
            HEADER_RECEIVED,
            COMPLEMENT_SENT,
            HANDSHAKE_COMPLETED,
            ESTABLISHED,
            FIN_SENT,
            CLOSED
        }
        ConnectionState m_ConnectionState = ConnectionState.WAITING_SYN;

        enum WindowScalingMode
        {
            Division, Multiplication, NoChange
        }
        WindowScalingMode m_WindowScalingMode = WindowScalingMode.NoChange;
        int m_WindowScalingArgument = 1;

        byte m_serverPort_higherByte;
        byte m_serverPort_lowerByte;

        byte m_hostPort_higherByte;
        byte m_hostPort_lowerByte;

        uint m_sequence_number;
        uint m_next_sequence_number;
        uint m_next_host_sequence_number;
        uint m_delta_seq;
        uint m_delta_ack;

        TcpControl m_Control;
        Guid m_ConnectionGuid;
        IPEndPoint m_HostIEP;
        byte m_EndpointType;
        ScheduledTask m_ComplementRepeatTask = null;
        byte[] m_PseudoHeader;

        public TcpProxyV6(IEPv6Key endpointKey)
        {
            m_EndpointKey = endpointKey;
        }

        public void Init(int serverPort)
        {
            byte[] portBytes = ByteConverter.GetBytes((ushort)serverPort);
            m_serverPort_higherByte = portBytes[0];
            m_serverPort_lowerByte = portBytes[1];

            LastSegmentReceivedSeconds = SystemClock.Seconds;
            m_SessionTimeoutSeconds = Constants.ProxyHandshakeTimeoutSeconds;
        }

        public void Remove()
        {
            m_ConnectionState = ConnectionState.CLOSED;
            LastSegmentReceivedSeconds = -(Constants.ProxySessionTimeoutSeconds + Constants.TcpProxySessionRenewSeconds);
            TcpDispatcher.RemoveProxyV6(m_EndpointKey);
        }

        ITcpProxy m_PeerProxy = null;
        public ITcpProxy PeerProxy
        {
            get { return m_PeerProxy; }
        }

        uint m_initial_sequence_number;
        public uint InitialSequenceNumber
        {
            get { return m_initial_sequence_number; }
        }

        uint m_initial_host_sequence_number;
        public uint InitialHostSequenceNumber
        {
            get { return m_initial_host_sequence_number; }
        }

        #region TCP Options

        VirtualTCPOptions m_VirtualTCPOptions = null;
        public VirtualTCPOptions VrtTCPOptions
        {
            get { return m_VirtualTCPOptions; }
            set { m_VirtualTCPOptions = value; }
        }

        TCPOptions m_TCPOptions = null;
        public TCPOptions HostTCPOptions
        {
            get { return m_TCPOptions; }
            set { m_TCPOptions = value; }
        }

        #endregion        
        
        #region Lifetime control

        public long LastSegmentReceivedSeconds;
        long m_SessionTimeoutSeconds;

        public bool IsAlive(long currentSeconds)
        {
            if (currentSeconds < LastSegmentReceivedSeconds + m_SessionTimeoutSeconds)
                return true;
            
            if (m_ConnectionState != ConnectionState.CLOSED)
                TcpDispatcher.RemoveProxyV6(m_EndpointKey);
            
            return false;
        }

        #endregion

        public void OnAccepted()
        {
            m_ConnectionState = ConnectionState.COMPLEMENT_SENT;
            SendComplement(1);
        }

        public void AdjustWindowScale(int peerWindowScale)
        {
            if (m_VirtualTCPOptions.WindowScale < peerWindowScale)
            {
                m_WindowScalingMode = WindowScalingMode.Multiplication;

                int scale = peerWindowScale - m_VirtualTCPOptions.WindowScale;
                for (int i = 1; i <= scale; i++)
                    m_WindowScalingArgument = m_WindowScalingArgument * 2;
            }
            else if (m_VirtualTCPOptions.WindowScale > peerWindowScale)
            {
                m_WindowScalingMode = WindowScalingMode.Division;

                int scale = m_VirtualTCPOptions.WindowScale - peerWindowScale;
                for (int i = 1; i <= scale; i++)
                    m_WindowScalingArgument = m_WindowScalingArgument * 2;
            }
        }

        public void SetPeerProxy(ITcpProxy peerProxy)
        {
            m_PeerProxy = peerProxy;
            unchecked
            {
                m_delta_seq = m_initial_sequence_number - m_PeerProxy.InitialHostSequenceNumber;
                m_delta_ack = m_initial_host_sequence_number - m_PeerProxy.InitialSequenceNumber;
            }
            m_ConnectionState = ConnectionState.ESTABLISHED;
        }

        public void HandleSegment(SocketAsyncEventArgs saea)
        {
            if (m_ConnectionState == ConnectionState.ESTABLISHED)
            {
                LastSegmentReceivedSeconds = SystemClock.Seconds;
                m_PeerProxy.SendSegment(saea, saea.Offset, saea.BytesTransferred);
            }
            else
            {
                if (m_ConnectionState == ConnectionState.WAITING_SYN)
                {
                    HandleState_WaitingSyn(saea);
                }
                else if (m_ConnectionState == ConnectionState.WAITING_HEADER)
                {
                    HandleState_WaitingHeader(saea);
                }
                else if (m_ConnectionState == ConnectionState.COMPLEMENT_SENT)
                {
                    HandleState_ComplementSent(saea);
                }
                else if (m_ConnectionState == ConnectionState.FIN_SENT)
                {
                    HandleState_FinSent(saea);
                }
                else
                {
                    TCPSaeaPool.Add(saea);
                }
            }
        }

        public void SendSegment(SocketAsyncEventArgs saea, int tcpOffset, int size)
        {
            byte[] segment = saea.Buffer;

            // set source port
            segment[tcpOffset] = m_serverPort_higherByte;
            segment[tcpOffset + 1] = m_serverPort_lowerByte;

            // set destination port
            segment[tcpOffset + 2] = m_hostPort_higherByte;
            segment[tcpOffset + 3] = m_hostPort_lowerByte;

            int TCP_FLAGS = segment[tcpOffset + 13];

            if ((TCP_FLAGS & ACK) != 0)
            {
                unchecked
                {
                    uint sequence_number = ByteConverter.ToUInt32(segment, tcpOffset + 4) + m_delta_seq;
                    Buffer.BlockCopy(ByteConverter.GetBytes(sequence_number), 0, segment, tcpOffset + 4, 4);

                    uint acknowledgment_number = ByteConverter.ToUInt32(segment, tcpOffset + 8) + m_delta_ack;
                    Buffer.BlockCopy(ByteConverter.GetBytes(acknowledgment_number), 0, segment, tcpOffset + 8, 4);
                }

                // Update SACK blocks
                if ((segment[tcpOffset + 12] & TCP_DATA_OFFSET_MASK) > 0x50)
                {
                    UpdateSACK(segment, tcpOffset, size);
                }
            }
            else
            {
                unchecked
                {
                    uint sequence_number = ByteConverter.ToUInt32(segment, tcpOffset + 4) + m_delta_seq;
                    Buffer.BlockCopy(ByteConverter.GetBytes(sequence_number), 0, segment, tcpOffset + 4, 4);
                    Buffer.BlockCopy(ByteConverter.GetBytes(sequence_number), 0, segment, tcpOffset + 8, 4);
                }
            }

            if (m_WindowScalingMode != WindowScalingMode.NoChange)
            {
                if (m_WindowScalingMode == WindowScalingMode.Division)
                {
                    int window = ByteConverter.ToUInt16(segment, tcpOffset + 14);
                    int updatedWindow = window / m_WindowScalingArgument;

                    if (window % m_WindowScalingArgument != 0)
                        updatedWindow++;

                    Buffer.BlockCopy(ByteConverter.GetBytes((UInt16)updatedWindow), 0, segment, tcpOffset + 14, 2);
                }
                else // m_WindowScalingMode == WindowScalingMode.Multiplication
                {
                    int window = ByteConverter.ToUInt16(segment, tcpOffset + 14);
                    int updatedWindow = window * m_WindowScalingArgument;

                    if (updatedWindow > 65535)
                        updatedWindow = 65535;

                    Buffer.BlockCopy(ByteConverter.GetBytes((UInt16)updatedWindow), 0, segment, tcpOffset + 14, 2);
                }
            }

            segment[tcpOffset + 16] = 0;
            segment[tcpOffset + 17] = 0;

            ushort checksum = Algorithms.ChecksumTcpV6(m_PseudoHeader, segment, tcpOffset, size);
            Buffer.BlockCopy(ByteConverter.GetBytes(checksum), 0, segment, tcpOffset + 16, 2);

            saea.SetBuffer(tcpOffset, size);
            saea.RemoteEndPoint = m_HostIEP;

            RawTcpBindingV6.Send(saea);
        }

        const byte C_OptionKind_SACK = 5;
        const byte C_OptionKind_EOL = 0;
        const byte C_OptionKind_NoP = 1;

        void UpdateSACK(byte[] segment, int tcpOffset, int size)
        {
            int upper_bound = tcpOffset + (size < 60 ? size : 60);
            int options_offset = tcpOffset + 20;

            while (options_offset < upper_bound)
            {
                byte OptionKind = segment[options_offset];

                if (OptionKind == C_OptionKind_SACK)
                {
                    int sack_upper_bound = options_offset + segment[options_offset + 1];
                    if (sack_upper_bound > upper_bound)
                        return;

                    int sack_offset = options_offset + 2;

                    while (sack_offset < sack_upper_bound)
                    {
                        uint acknowledgmentNumber = ByteConverter.ToUInt32(segment, sack_offset) + m_delta_ack;
                        Buffer.BlockCopy(ByteConverter.GetBytes(acknowledgmentNumber), 0, segment, sack_offset, 4);

                        sack_offset += 4;
                    }

                    return;
                }
                else
                {
                    if (OptionKind == C_OptionKind_NoP)
                    {
                        options_offset++;
                    }
                    else if (OptionKind != C_OptionKind_EOL)
                    {
                        int option_length = segment[options_offset + 1];
                        options_offset += option_length;
                    }
                    else
                    {
                        break;
                    }
                }
            }
        }

        void HandleState_WaitingSyn(SocketAsyncEventArgs saea)
        {
            byte[] segment = saea.Buffer;
            int tcpOffset = saea.Offset;

            int FLAG_BITS = segment[tcpOffset + 13];
            if ((FLAG_BITS & SYN) == 0)
            {
                TCPSaeaPool.Add(saea);
                return;
            }

            m_HostIEP = (IPEndPoint)saea.RemoteEndPoint;

            m_hostPort_higherByte = segment[tcpOffset];
            m_hostPort_lowerByte = segment[tcpOffset + 1];

            uint host_sequence_number = ByteConverter.ToUInt32(segment, tcpOffset + 4);
            unchecked { m_next_host_sequence_number = host_sequence_number + 1; }

            m_PseudoHeader = TcpPseudoHeader.GetPHv6(m_HostIEP.Address);

            m_sequence_number = ByteConverter.ToUInt32(Randomizer.ByteString(4), 0);
            unchecked { m_next_sequence_number = m_sequence_number + 1; }

            m_initial_sequence_number = m_sequence_number;
            m_initial_host_sequence_number = host_sequence_number;

            m_ConnectionState = ConnectionState.WAITING_HEADER;

            BuildSynAckHeader(saea);
            RawTcpBindingV6.Send(saea);
        }

        void HandleState_WaitingHeader(SocketAsyncEventArgs saea)
        {
            byte[] segment = saea.Buffer;
            int tcpOffset = saea.Offset;
            int FLAG_BITS = segment[tcpOffset + 13];

            if ((FLAG_BITS & RST_SYN_FIN) != 0)
            {
                if ((FLAG_BITS & SYN) != 0)
                {
                    uint received_seq_num = ByteConverter.ToUInt32(segment, tcpOffset + 4);
                    if (received_seq_num == m_initial_host_sequence_number)
                    {
                        BuildSynAckHeader(saea);
                        RawTcpBindingV6.Send(saea);
                        return;
                    }
                }
                else if ((FLAG_BITS & FIN) != 0)
                {
                    uint received_seq_num = ByteConverter.ToUInt32(segment, tcpOffset + 4);
                    if (received_seq_num == m_next_host_sequence_number)
                    {
                        m_ConnectionState = ConnectionState.FIN_SENT;

                        int tcp_header_size = (segment[tcpOffset + 12] >> 4) * 4;                        
                        int dataLength = saea.BytesTransferred - tcp_header_size;
                        unchecked { received_seq_num += ((uint)(1 + dataLength)); }

                        uint received_ack_num = ByteConverter.ToUInt32(segment, tcpOffset + 8);

                        BuildFinAckHeader(saea, received_ack_num, received_seq_num);
                        RawTcpBindingV6.Send(saea);
                        return;
                    }
                }

                TCPSaeaPool.Add(saea);
                return;
            }

            if ((FLAG_BITS & ACK) != 0)
            {
                uint received_seq_num = ByteConverter.ToUInt32(segment, tcpOffset + 4);
                uint received_ack_num = ByteConverter.ToUInt32(segment, tcpOffset + 8);

                if (received_seq_num == m_next_host_sequence_number && received_ack_num == m_next_sequence_number)
                {
                    int tcp_header_size = (segment[tcpOffset + 12] >> 4) * 4;
                    int data_offset = tcpOffset + tcp_header_size;
                    int dataLength = saea.BytesTransferred - tcp_header_size;

                    if (dataLength == 17)
                    {
                        m_EndpointType = segment[data_offset];
                        data_offset++;

                        m_ConnectionGuid = ByteConverter.ToGuid(segment, data_offset);

                        m_Control = TcpDispatcher.FindControl(m_ConnectionGuid);
                        if (m_Control == null)
                        {
                            BuildRstHeader(saea);
                            RawTcpBindingV6.Send(saea);
                            Remove();
                            return;
                        }

                        if (m_EndpointType == Constants.TcpProxy.SERVICE_PROXY_ENDPOINT)
                        {
                            m_ConnectionState = ConnectionState.HEADER_RECEIVED;

                            m_sequence_number = m_next_sequence_number;
                            unchecked { m_next_sequence_number += 17; }
                            unchecked { m_next_host_sequence_number += 17; }

                            BuildAckHeader(saea);
                            RawTcpBindingV6.Send(saea);

                            m_Control.SetServiceProxy(this);
                            return;
                        }

                        if (m_EndpointType == Constants.TcpProxy.CLIENT_PROXY_ENDPOINT)
                        {
                            m_ConnectionState = ConnectionState.HEADER_RECEIVED;

                            m_sequence_number = m_next_sequence_number;
                            unchecked { m_next_sequence_number += 17; }
                            unchecked { m_next_host_sequence_number += 17; }

                            BuildAckHeader(saea);
                            RawTcpBindingV6.Send(saea);

                            m_Control.SetClientProxy(this);
                            return;
                        }
                    }
                    else if (dataLength == 0)
                    {
                        TCPSaeaPool.Add(saea);
                        return;
                    }

                    BuildRstHeader(saea);
                    RawTcpBindingV6.Send(saea);
                    Remove();
                    return;
                }
            }

            TCPSaeaPool.Add(saea);
        }

        void SendComplement(object state)
        {
            if (m_ConnectionState != ConnectionState.COMPLEMENT_SENT)
                return;

            int waitSeconds = ((int)state) * 2;
            if (waitSeconds > 16)
                return;

            SocketAsyncEventArgs saea = TCPSaeaPool.Get();
            if (saea == null)
            {
                Remove();
                return;
            }

            m_ComplementRepeatTask = new ScheduledTask(SendComplement, waitSeconds);
            Softnet.ServerKit.TaskScheduler.Add(m_ComplementRepeatTask, waitSeconds);

            byte[] segment = saea.Buffer;
            int dataOffset = saea.Offset + 20;

            segment[dataOffset] = (m_EndpointType == Constants.TcpProxy.CLIENT_PROXY_ENDPOINT ? Constants.TcpProxy.SERVICE_PROXY_ENDPOINT : Constants.TcpProxy.CLIENT_PROXY_ENDPOINT);
            Buffer.BlockCopy(ByteConverter.GetBytes(m_ConnectionGuid), 0, segment, dataOffset + 1, 16);

            saea.RemoteEndPoint = m_HostIEP;
            BuildPshAckHeader(saea, 17);
            RawTcpBindingV6.Send(saea);
        }

        void HandleState_ComplementSent(SocketAsyncEventArgs saea)
        {
            byte[] segment = saea.Buffer;
            int tcpOffset = saea.Offset;
            int FLAG_BITS = segment[tcpOffset + 13];

            if ((FLAG_BITS & RST_SYN_FIN) != 0)
            {
                if ((FLAG_BITS & FIN) != 0)
                {
                    uint received_seq_num = ByteConverter.ToUInt32(segment, tcpOffset + 4);
                    if (received_seq_num == m_next_host_sequence_number)
                    {
                        m_ConnectionState = ConnectionState.FIN_SENT;

                        if (m_ComplementRepeatTask != null)
                            m_ComplementRepeatTask.Cancel();

                        int tcp_header_size = (segment[tcpOffset + 12] >> 4) * 4;
                        int dataLength = saea.BytesTransferred - tcp_header_size;
                        unchecked { received_seq_num += ((uint)(1 + dataLength)); }
                        uint received_ack_num = ByteConverter.ToUInt32(segment, tcpOffset + 8);

                        BuildFinAckHeader(saea, received_ack_num, received_seq_num);
                        RawTcpBindingV6.Send(saea);
                        return;
                    }
                }

                TCPSaeaPool.Add(saea);
                return;
            }

            if ((FLAG_BITS & ACK) != 0)
            {
                uint received_ack_num = ByteConverter.ToUInt32(segment, tcpOffset + 8);
                if (received_ack_num == m_next_sequence_number)
                {
                    m_ConnectionState = ConnectionState.HANDSHAKE_COMPLETED;

                    if (m_ComplementRepeatTask != null)
                        m_ComplementRepeatTask.Cancel();

                    LastSegmentReceivedSeconds = SystemClock.Seconds;
                    m_SessionTimeoutSeconds = Constants.ProxySessionTimeoutSeconds;

                    if (m_EndpointType == Constants.TcpProxy.SERVICE_PROXY_ENDPOINT)
                    {
                        m_Control.OnServiceProxyReady();
                    }
                    else
                    {
                        m_Control.OnClientProxyReady();
                    }
                }
                else if (received_ack_num == m_sequence_number)
                {
                    int tcp_header_size = (segment[tcpOffset + 12] >> 4) * 4;
                    int dataLength = saea.BytesTransferred - tcp_header_size;

                    if (dataLength == 17)
                    {
                        BuildAckHeader(saea);
                        RawTcpBindingV6.Send(saea);
                        return;
                    }
                }
            }

            TCPSaeaPool.Add(saea);
        }

        void HandleState_FinSent(SocketAsyncEventArgs saea)
        {
            byte[] segment = saea.Buffer;
            int tcpOffset = saea.Offset;
            int FLAG_BITS = segment[tcpOffset + 13];

            if ((FLAG_BITS & FIN) != 0)
            {
                uint received_seq_num = ByteConverter.ToUInt32(segment, tcpOffset + 4);
                uint received_ack_num = ByteConverter.ToUInt32(segment, tcpOffset + 8);

                int tcp_header_size = (segment[tcpOffset + 12] >> 4) * 4;
                int dataLength = saea.BytesTransferred - tcp_header_size;
                unchecked { received_seq_num += ((uint)(1 + dataLength)); }

                BuildFinAckHeader(saea, received_ack_num, received_seq_num);
                RawTcpBindingV6.Send(saea);
                return;
            }

            TCPSaeaPool.Add(saea);
        }

        //------------------------------------------------------------------------------------------------------------------------------------------------------------

        void BuildSynAckHeader(SocketAsyncEventArgs saea)
        {
            byte[] segment = saea.Buffer;
            int offset = saea.Offset;

            // set source port
            segment[offset] = m_serverPort_higherByte;
            segment[offset + 1] = m_serverPort_lowerByte;

            // set destination port
            segment[offset + 2] = m_hostPort_higherByte;
            segment[offset + 3] = m_hostPort_lowerByte;

            // set SequenceNumber
            byte[] seq_num_bytes = ByteConverter.GetBytes(m_sequence_number);
            Buffer.BlockCopy(seq_num_bytes, 0, segment, offset + 4, 4);

            // set AcknowledgementNumber
            byte[] ack_num_bytes = ByteConverter.GetBytes(m_next_host_sequence_number);
            Buffer.BlockCopy(ack_num_bytes, 0, segment, offset + 8, 4);

            segment[offset + 13] = SYN_ACK_BYTE;

            // Window = 16384
            segment[offset + 14] = 0x40;
            segment[offset + 15] = 0;

            // checksum
            segment[offset + 16] = 0;
            segment[offset + 17] = 0;

            // urgent pointer
            segment[offset + 18] = 0;
            segment[offset + 19] = 0;

            // Max Segment Size
            segment[offset + 20] = 2;
            segment[offset + 21] = 4;
            Buffer.BlockCopy(ByteConverter.GetBytes((UInt16)m_VirtualTCPOptions.MSS), 0, segment, offset + 22, 2);

            int header_size = 24;

            if (m_VirtualTCPOptions.WindowScaleSupported)
            {
                segment[offset + header_size] = 1;     // NoP

                segment[offset + header_size + 1] = 3;
                segment[offset + header_size + 2] = 3;
                segment[offset + header_size + 3] = (byte)m_VirtualTCPOptions.WindowScale;

                header_size += 4;
            }

            if (m_VirtualTCPOptions.SACKPermitted)
            {
                segment[offset + header_size] = 1;     // NoP
                segment[offset + header_size + 1] = 1; // NoP

                segment[offset + header_size + 2] = 4;
                segment[offset + header_size + 3] = 2;

                header_size += 4;
            }

            if (header_size == 24)
            {
                segment[offset + 12] = TCP_DATA_OFFSET_24;
            }
            else if (header_size == 28)
            {
                segment[offset + 12] = TCP_DATA_OFFSET_28;
            }
            else // header_size == 32
            {
                segment[offset + 12] = TCP_DATA_OFFSET_32;
            }

            UInt16 checksum = Algorithms.ChecksumTcpV6(m_PseudoHeader, segment, offset, header_size);
            Buffer.BlockCopy(ByteConverter.GetBytes(checksum), 0, segment, offset + 16, 2);

            saea.SetBuffer(offset, header_size);
        }

        void BuildAckHeader(SocketAsyncEventArgs saea)
        {
            byte[] segment = saea.Buffer;
            int offset = saea.Offset;

            // set source port
            segment[offset] = m_serverPort_higherByte;
            segment[offset + 1] = m_serverPort_lowerByte;

            // set destination port
            segment[offset + 2] = m_hostPort_higherByte;
            segment[offset + 3] = m_hostPort_lowerByte;

            // set SequenceNumber
            byte[] sequenceNumberBytes = ByteConverter.GetBytes(m_sequence_number);
            Buffer.BlockCopy(sequenceNumberBytes, 0, segment, offset + 4, 4);

            // set AcknowledgementNumber
            byte[] acknowledgementNumberBytes = ByteConverter.GetBytes(m_next_host_sequence_number);
            Buffer.BlockCopy(acknowledgementNumberBytes, 0, segment, offset + 8, 4);

            segment[offset + 12] = TCP_DATA_OFFSET_20;
            segment[offset + 13] = ACK_BYTE;

            // Window = 16384
            segment[offset + 14] = 0x40;
            segment[offset + 15] = 0;

            // checksum
            segment[offset + 16] = 0;
            segment[offset + 17] = 0;

            // urgent pointer
            segment[offset + 18] = 0;
            segment[offset + 19] = 0;

            UInt16 checksum = Algorithms.ChecksumTcpV6(m_PseudoHeader, segment, offset, 20);
            Buffer.BlockCopy(ByteConverter.GetBytes(checksum), 0, segment, offset + 16, 2);

            saea.SetBuffer(offset, 20);
        }

        void BuildPshAckHeader(SocketAsyncEventArgs saea, int dataSize)
        {
            byte[] segment = saea.Buffer;
            int offset = saea.Offset;

            // set source port
            segment[offset] = m_serverPort_higherByte;
            segment[offset + 1] = m_serverPort_lowerByte;

            // set destination port
            segment[offset + 2] = m_hostPort_higherByte;
            segment[offset + 3] = m_hostPort_lowerByte;

            // set SequenceNumber
            byte[] seq_num_bytes = ByteConverter.GetBytes(m_sequence_number);
            Buffer.BlockCopy(seq_num_bytes, 0, segment, offset + 4, 4);

            // set AcknowledgementNumber
            byte[] ack_num_bytes = ByteConverter.GetBytes(m_next_host_sequence_number);
            Buffer.BlockCopy(ack_num_bytes, 0, segment, offset + 8, 4);

            segment[offset + 12] = TCP_DATA_OFFSET_20;
            segment[offset + 13] = PSH_ACK_BYTE;

            // Window = 16384
            segment[offset + 14] = 0x40;
            segment[offset + 15] = 0;

            // checksum
            segment[offset + 16] = 0;
            segment[offset + 17] = 0;

            // urgent pointer
            segment[offset + 18] = 0;
            segment[offset + 19] = 0;

            UInt16 checksum = Algorithms.ChecksumTcpV6(m_PseudoHeader, segment, offset, 20 + dataSize);
            Buffer.BlockCopy(ByteConverter.GetBytes(checksum), 0, segment, offset + 16, 2);

            saea.SetBuffer(offset, 20 + dataSize);
        }

        void BuildFinAckHeader(SocketAsyncEventArgs saea, uint seq_num, uint ack_num)
        {
            byte[] segment = saea.Buffer;
            int offset = saea.Offset;

            // set source port
            segment[offset] = m_serverPort_higherByte;
            segment[offset + 1] = m_serverPort_lowerByte;

            // set destination port
            segment[offset + 2] = m_hostPort_higherByte;
            segment[offset + 3] = m_hostPort_lowerByte;

            // set SequenceNumber
            byte[] sequenceNumberBytes = ByteConverter.GetBytes(seq_num);
            Buffer.BlockCopy(sequenceNumberBytes, 0, segment, offset + 4, 4);

            // set AcknowledgementNumber
            byte[] acknowledgementNumberBytes = ByteConverter.GetBytes(ack_num);
            Buffer.BlockCopy(acknowledgementNumberBytes, 0, segment, offset + 8, 4);

            segment[offset + 12] = TCP_DATA_OFFSET_20;
            segment[offset + 13] = FIN_ACK_BYTE;

            // Window = 16384
            segment[offset + 14] = 0x40;
            segment[offset + 15] = 0;

            // checksum
            segment[offset + 16] = 0;
            segment[offset + 17] = 0;

            // urgent pointer
            segment[offset + 18] = 0;
            segment[offset + 19] = 0;

            UInt16 checksum = Algorithms.ChecksumTcpV6(m_PseudoHeader, segment, offset, 20);
            Buffer.BlockCopy(ByteConverter.GetBytes(checksum), 0, segment, offset + 16, 2);

            saea.SetBuffer(offset, 20);
        }

        void BuildRstHeader(SocketAsyncEventArgs saea)
        {
            byte[] segment = saea.Buffer;
            int offset = saea.Offset;

            UInt32 received_seq_num = ByteConverter.ToUInt32(segment, offset + 4);

            // set source port
            segment[offset] = m_serverPort_higherByte;
            segment[offset + 1] = m_serverPort_lowerByte;

            // set destination port
            segment[offset + 2] = m_hostPort_higherByte;
            segment[offset + 3] = m_hostPort_lowerByte;

            // set SequenceNumber  
            byte[] receivedSequenceNumberBytes = ByteConverter.GetBytes(received_seq_num);
            Buffer.BlockCopy(receivedSequenceNumberBytes, 0, segment, offset + 4, 4);

            // set AcknowledgementNumber
            Buffer.BlockCopy(receivedSequenceNumberBytes, 0, segment, offset + 8, 4);

            segment[offset + 12] = TCP_DATA_OFFSET_20;
            segment[offset + 13] = RST_BYTE;

            // Window
            segment[offset + 14] = 0x04;
            segment[offset + 15] = 0;

            // checksum
            segment[offset + 16] = 0;
            segment[offset + 17] = 0;

            // urgent pointer
            segment[offset + 18] = 0;
            segment[offset + 19] = 0;

            UInt16 checksum = Algorithms.ChecksumTcpV6(m_PseudoHeader, segment, offset, 20);
            Buffer.BlockCopy(ByteConverter.GetBytes(checksum), 0, segment, offset + 16, 2);

            saea.SetBuffer(offset, 20);
        }

        const byte TCP_DATA_OFFSET_20 = 0x50; // 01010000  - 5 x 4 = 20 bytes
        const byte TCP_DATA_OFFSET_24 = 0x60; // 01100000  - 6 x 4 = 24 bytes
        const byte TCP_DATA_OFFSET_28 = 0x70; // 01110000  - 7 x 4 = 28 bytes
        const byte TCP_DATA_OFFSET_32 = 0x80; // 10000000  - 8 x 4 = 32 bytes

        const int TCP_DATA_OFFSET_MASK = 0xF0;

        const int ACK = 0x10;
        const int SYN = 0x02;
        const int RST = 0x04;
        const int FIN = 0x01;
        const int RST_SYN_FIN = 0x07;

        const byte ACK_BYTE = 0x10;
        const byte FIN_ACK_BYTE = 0x11;
        const byte SYN_ACK_BYTE = 0x12;
        const byte PSH_ACK_BYTE = 0x18;
        const byte RST_BYTE = 0x04;
    }
}
