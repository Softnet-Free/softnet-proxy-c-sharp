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
    class UdpProxyV6 : IUdpProxy, Monitorable
    {
        IEPv6Key m_EndpointKey;
        IPEndPoint m_HostIEP;
        IUdpProxy m_PeerProxy = null;
        bool m_closed = false;

        byte host_port_higher_byte;
        byte host_port_lower_byte;
        byte[] m_pseudoHeader = null;

        public UdpProxyV6(IEPv6Key endpointKey, IPEndPoint hostIEP, byte[] hostIEPBytes)
        {
            m_EndpointKey = endpointKey;
            m_HostIEP = hostIEP;
            host_port_higher_byte = hostIEPBytes[16];
            host_port_lower_byte = hostIEPBytes[17];
        }

        public void Init()
        {
            LastPacketReceiveTime = SystemClock.Seconds;
            m_pseudoHeader = UdpPseudoHeader.GetPHv6(m_HostIEP.Address);
        }

        public void Close()
        {
            m_closed = true;
            LastPacketReceiveTime = -Constants.UdpProxySessionTimeoutSeconds;
        }

        #region Lifetime control

        long LastPacketReceiveTime;
        public bool IsAlive(long currentSeconds)
        {
            if (currentSeconds < LastPacketReceiveTime + Constants.UdpProxySessionTimeoutSeconds)
                return true;

            m_closed = true;
            UdpDispatcher.RemoveProxyV6(this, m_EndpointKey);
            return false;
        }

        #endregion

        public void SetPeerProxy(IUdpProxy peerProxy)
        {
            m_PeerProxy = peerProxy;
        }

        public void HandlePacket(SocketAsyncEventArgs saea)
        {
            if (m_closed == false)
            {
                LastPacketReceiveTime = SystemClock.Seconds;
                m_PeerProxy.SendPacket(saea, saea.Offset, saea.BytesTransferred, LastPacketReceiveTime);
            }
            else
            {
                UDPSaeaPool.Add(saea);
            }
        }

        public void SendPacket(SocketAsyncEventArgs saea, int udpOffset, int size, long currentTime)
        {
            if (m_closed == false)
            {
                LastPacketReceiveTime = currentTime;

                byte[] buffer = saea.Buffer;
                buffer[udpOffset] = UdpDispatcher.server_port_higher_byte;
                buffer[udpOffset + 1] = UdpDispatcher.server_port_lower_byte;
                buffer[udpOffset + 2] = host_port_higher_byte;
                buffer[udpOffset + 3] = host_port_lower_byte;

                buffer[udpOffset + 6] = 0;
                buffer[udpOffset + 7] = 0;

                UInt16 checksum = Algorithms.ChecksumUdpV6(m_pseudoHeader, buffer, udpOffset, size);
                Buffer.BlockCopy(ByteConverter.GetBytes(checksum), 0, buffer, udpOffset + 6, 2);

                saea.SetBuffer(udpOffset, size);
                saea.RemoteEndPoint = m_HostIEP;
                RawUdpBindingV6.Send(saea);
            }
            else
            {
                UDPSaeaPool.Add(saea);
            }
        }
    }
}
