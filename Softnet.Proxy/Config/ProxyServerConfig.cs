using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Softnet.Proxy.Config
{
    public class ProxyServerConfig
    {
        public string SecretKey { get; set; }
        public int MaxSessions { get; set; }
        public string IPv6 { get; set; }
        public string IPv4 { get; set; }
        public TcpConfig TcpConfig { get; set; }
        public UdpConfig UdpConfig { get; set; }
    }
}
