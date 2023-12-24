using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Softnet.Proxy.Config
{
    public class TcpConfig
    {
        public int MaxBufferedPackets { get; set; }
        public int TCPv6_MSS { get; set; }
        public int TCPv4_MSS { get; set; }
    }
}
