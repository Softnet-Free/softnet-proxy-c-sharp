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

namespace Softnet.Proxy
{
    static class Algorithms
    {
        public static UInt16 ChecksumTcpV6(byte[] pseudoHeader, byte[] tcpPacket, int offset, int size)
        {
            Buffer.BlockCopy(Softnet.ServerKit.ByteConverter.GetBytes(size), 0, pseudoHeader, 32, 4);

            unchecked
            {
                UInt32 Sum = 0;

                // Accumulate checksum of pseudo header 
                int upper_index = pseudoHeader.Length - 1;
                for (int i = 0; i < upper_index; i += 2)
                {
                    Sum += ((UInt32)pseudoHeader[i]) << 8;
                    Sum += pseudoHeader[i + 1];
                }

                // Accumulate checksum of tcp packet 
                upper_index = offset + size - 1;
                for (int i = offset; i < upper_index; i += 2)
                {
                    Sum += ((UInt32)tcpPacket[i]) << 8;
                    Sum += tcpPacket[i + 1];
                }

                // handle odd-sized case 
                if (size % 2 == 1)
                    Sum += ((UInt32)tcpPacket[upper_index]) << 8;

                // fold to get the ones-complement result 
                while (Sum >> 16 != 0)
                {
                    Sum = (Sum & 0xFFFF) + (Sum >> 16);
                }

                // invert to get the negative in ones-complement arithmetic
                return (UInt16)(~Sum);
            }
        }

        public static UInt16 ChecksumTcpV4(byte[] pseudoHeader, byte[] tcpPacket, int offset, int size)
        {
            Buffer.BlockCopy(Softnet.ServerKit.ByteConverter.GetBytes((UInt16)size), 0, pseudoHeader, 10, 2);
            unchecked
            {
                UInt32 Sum = 0;

                // Accumulate the checksum of the pseudo header 
                int upper_index = pseudoHeader.Length - 1;
                for (int i = 0; i < upper_index; i += 2)
                {
                    Sum += ((UInt32)pseudoHeader[i]) << 8;
                    Sum += pseudoHeader[i + 1];
                }

                // Accumulate the checksum of the tcp packet
                upper_index = offset + size - 1;
                for (int i = offset; i < upper_index; i += 2)
                {
                    Sum += ((UInt32)tcpPacket[i]) << 8;
                    Sum += tcpPacket[i + 1];
                }

                // handle odd-sized case 
                if (size % 2 == 1)
                    Sum += ((UInt32)tcpPacket[upper_index]) << 8;

                // fold to get the ones-complement result 
                while (Sum >> 16 != 0)
                {
                    Sum = (Sum & 0xFFFF) + (Sum >> 16);
                }

                // invert to get the negative in ones-complement arithmetic
                return (UInt16)(~Sum);
            }
        }

        public static UInt16 ChecksumUdpV6(byte[] pseudoHeader, byte[] udpPacket, int offset, int size)
        {
            Buffer.BlockCopy(Softnet.ServerKit.ByteConverter.GetBytes(size), 0, pseudoHeader, 32, 4);

            unchecked
            {
                UInt32 Sum = 0;

                // Accumulate checksum of pseudo header 
                int upper_index = pseudoHeader.Length - 1;
                for (int i = 0; i < upper_index; i += 2)
                {
                    Sum += ((UInt32)pseudoHeader[i]) << 8;
                    Sum += pseudoHeader[i + 1];
                }

                // Accumulate checksum of tcp packet 
                upper_index = offset + size - 1;
                for (int i = offset; i < upper_index; i += 2)
                {
                    Sum += ((UInt32)udpPacket[i]) << 8;
                    Sum += udpPacket[i + 1];
                }

                // handle odd-sized case 
                if (size % 2 == 1)
                    Sum += ((UInt32)udpPacket[upper_index]) << 8;

                // fold to get the ones-complement result 
                while (Sum >> 16 != 0)
                {
                    Sum = (Sum & 0xFFFF) + (Sum >> 16);
                }

                // invert to get the negative in ones-complement arithmetic
                return (UInt16)(~Sum);
            }
        }

        public static UInt16 ChecksumUdpV4(byte[] pseudoHeader, byte[] udpPacket, int offset, int size)
        {
            Buffer.BlockCopy(Softnet.ServerKit.ByteConverter.GetBytes((UInt16)size), 0, pseudoHeader, 10, 2);
            unchecked
            {
                UInt32 Sum = 0;

                // Accumulate the checksum of the pseudo header 
                int upper_index = pseudoHeader.Length - 1;
                for (int i = 0; i < upper_index; i += 2)
                {
                    Sum += ((UInt32)pseudoHeader[i]) << 8;
                    Sum += pseudoHeader[i + 1];
                }

                // Accumulate the checksum of the udp packet
                upper_index = offset + size - 1;
                for (int i = offset; i < upper_index; i += 2)
                {
                    Sum += ((UInt32)udpPacket[i]) << 8;
                    Sum += udpPacket[i + 1];
                }

                // handle odd-sized case 
                if (size % 2 == 1)
                    Sum += ((UInt32)udpPacket[upper_index]) << 8;

                // fold to get the ones-complement result 
                while (Sum >> 16 != 0)
                {
                    Sum = (Sum & 0xFFFF) + (Sum >> 16);
                }

                // invert to get the negative in ones-complement arithmetic
                return (UInt16)(~Sum);
            }
        }

        public static int HashFnv1a(byte[] data)
        {
            unchecked
            {
                const int p = 16777619;
                int hash = (int)2166136261;

                for (int i = 0; i < data.Length; i++)
                    hash = (hash ^ data[i]) * p;

                hash += hash << 13;
                hash ^= hash >> 7;
                hash += hash << 3;
                hash ^= hash >> 17;
                hash += hash << 5;

                return hash;
            }
        }
    }
}
