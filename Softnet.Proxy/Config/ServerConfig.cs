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
using System.Configuration;
using System.ComponentModel;
using System.Globalization;

namespace Softnet.Proxy
{
    class ServerConfig : ConfigurationSection
    {
        [ConfigurationProperty("secretKey", IsRequired = true)]
        public string SecretKey
        {
            get
            {
                return (string)this["secretKey"];
            }
        }

        [ConfigurationProperty("maxSessions", IsRequired = true)]
        public int MaxSessions
        {
            get
            {
                return (int)this["maxSessions"];
            }
        }

        [ConfigurationProperty("IPv6", IsRequired = false)]
        [TypeConverter(typeof(IPConverter))]
        public System.Net.IPAddress IPv6
        {
            get
            {
                System.Net.IPAddress IP = (System.Net.IPAddress)base["IPv6"];
                if (IP != null)
                {
                    if (IP.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6)
                        throw new ConfigurationErrorsException("Invalid address family");
                    return IP;
                }
                else return null;
            }
        }

        [ConfigurationProperty("IPv4", IsRequired = false)]
        [TypeConverter(typeof(IPConverter))]
        public System.Net.IPAddress IPv4
        {
            get
            {
                System.Net.IPAddress IP = (System.Net.IPAddress)base["IPv4"];
                if (IP != null)
                {
                    if (IP.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                        throw new ConfigurationErrorsException("Invalid address family");
                    return IP;
                }
                else return null;
            }
        }

        [ConfigurationProperty("tcpConfig", IsRequired = true)]
        public TcpConfigElement TcpConfig
        {
            get
            {
                return (TcpConfigElement)this["tcpConfig"];
            }
        }

        [ConfigurationProperty("udpConfig", IsRequired = true)]
        public UdpConfigElement UdpConfig
        {
            get
            {
                return (UdpConfigElement)this["udpConfig"];
            }
        }
    }

    public class TcpConfigElement : ConfigurationElement
    {
        [ConfigurationProperty("maxBufferedPackets", IsRequired = true)]
        public int MaxBufferedPackets
        {
            get
            {
                return (int)this["maxBufferedPackets"];
            }
        }

        [ConfigurationProperty("TCPv6_MSS", IsRequired = false, DefaultValue = 1440)]
        public int TCPv6_MSS
        {
            get
            {
                return (int)this["TCPv6_MSS"];
            }
        }

        [ConfigurationProperty("TCPv4_MSS", IsRequired = false, DefaultValue = 1460)]
        public int TCPv4_MSS
        {
            get
            {
                return (int)this["TCPv4_MSS"];
            }
        }
    }

    public class UdpConfigElement : ConfigurationElement
    {
        [ConfigurationProperty("maxBufferedPackets", IsRequired = true)]
        public int MaxBufferedPackets
        {
            get
            {
                return (int)this["maxBufferedPackets"];
            }
        }

        [ConfigurationProperty("packetSize", IsRequired = false, DefaultValue = 4124)]
        public int PacketSize
        {
            get
            {
                return (int)this["packetSize"];
            }
        }
    }

    public sealed class IPConverter : ConfigurationConverterBase
    {
        public override bool CanConvertTo(ITypeDescriptorContext ctx, Type type)
        {
            return (type == typeof(string));
        }

        public override bool CanConvertFrom(ITypeDescriptorContext ctx, Type type)
        {
            return (type == typeof(string));
        }

        public override object ConvertTo(ITypeDescriptorContext ctx, CultureInfo ci, object value, Type type)
        {
            return ((System.Net.IPAddress)value).ToString();
        }

        public override object ConvertFrom(ITypeDescriptorContext ctx, CultureInfo ci, object value)
        {
            string str_ip = (string)value;
            if (str_ip == string.Empty)
                return null;
            return System.Net.IPAddress.Parse(str_ip);
        }
    }
}
