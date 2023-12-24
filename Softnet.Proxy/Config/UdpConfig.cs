using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Softnet.Proxy.Config
{
    public class UdpConfig
    {
        public int MaxBufferedPackets { get; set; }
        public int PacketSize { get; set; }
    }
}
