using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Sockets;
using System.Configuration;

using Softnet.ServerKit;
using Softnet.Proxy.Config;

namespace Softnet.Proxy
{
    public class WindowsBackgroundService : BackgroundService
    {
        private readonly ILogger<WindowsBackgroundService> _logger;
        private readonly IConfiguration _configuration;
        private readonly IOptionsMonitor<ProxyServerConfig> _proxyServerConfigMonitor;

        public WindowsBackgroundService(
            ILogger<WindowsBackgroundService> logger, 
            IConfiguration configuration, 
            IOptionsMonitor<ProxyServerConfig> proxyServerConfigMonitor)
        {
            _logger = logger;
            _configuration = configuration;
            _proxyServerConfigMonitor = proxyServerConfigMonitor;
            
            _proxyServerConfigMonitor.OnChange(serverConfig =>
            {
                try
                {
                    if (string.IsNullOrEmpty(serverConfig.SecretKey))
                        throw new InvalidOperationException("The secret key is not specified in the application settings");
                    SecretKey = Encoding.BigEndianUnicode.GetBytes(serverConfig.SecretKey);

                    if (!(1220 <= serverConfig.TcpConfig.TCPv6_MSS && serverConfig.TcpConfig.TCPv6_MSS <= 1440))
                        throw new InvalidOperationException("The TCP maximum segment size (MSS) on IPv6 must be in the range [1220 - 1440]");
                    Local_TCPv6_MSS = serverConfig.TcpConfig.TCPv6_MSS;

                    if (!(536 <= serverConfig.TcpConfig.TCPv4_MSS && serverConfig.TcpConfig.TCPv6_MSS <= 1460))
                        throw new InvalidOperationException("The TCP maximum segment size (MSS) on IPv4 must be in the range [536 - 1460]");
                    Local_TCPv4_MSS = serverConfig.TcpConfig.TCPv4_MSS;

                    if (Local_TCPv6_MSS < Local_TCPv4_MSS)
                        Local_TCPv6v4_MSS = Local_TCPv6_MSS;
                    else
                        Local_TCPv6v4_MSS = Local_TCPv4_MSS;
                }
                catch (InvalidOperationException e)
                {
                    _logger.LogInformation("Failed to update an app-settings parameter: {error}", e.Message);
                }
            });
        }

        public static byte[] SecretKey = null!;

        public static byte[]? LocalIPv6Bytes;
        public static byte[]? LocalIPv4Bytes;

        public static int Local_TCPv6_MSS = 1440;
        public static int Local_TCPv4_MSS = 1460;
        public static int Local_TCPv6v4_MSS = 1440;

        static bool s_isIPv6Bound;
        static bool s_isIPv4Bound;

        public static SaeaPool CSaeaPool = null!;

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Service started at: {time}", DateTimeOffset.Now);

            try
            {
                var serverConfig = _configuration.GetSection("ProxyServer").Get<ProxyServerConfig>();

                if (string.IsNullOrEmpty(serverConfig.SecretKey))
                    throw new InvalidOperationException("The secret key is not specified in the application settings");
                SecretKey = Encoding.BigEndianUnicode.GetBytes(serverConfig.SecretKey);

                LocalIPv6Bytes = string.IsNullOrEmpty(serverConfig.IPv6) ? null : IPAddress.Parse(serverConfig.IPv6).GetAddressBytes();
                LocalIPv4Bytes = string.IsNullOrEmpty(serverConfig.IPv4) ? null : IPAddress.Parse(serverConfig.IPv4).GetAddressBytes();

                if (!(1220 <= serverConfig.TcpConfig.TCPv6_MSS && serverConfig.TcpConfig.TCPv6_MSS <= 1440))
                    throw new InvalidOperationException("The TCP maximum segment size (MSS) on IPv6 must be in the range [1220 - 1440]");
                Local_TCPv6_MSS = serverConfig.TcpConfig.TCPv6_MSS;

                if (!(536 <= serverConfig.TcpConfig.TCPv4_MSS && serverConfig.TcpConfig.TCPv6_MSS <= 1460))
                    throw new InvalidOperationException("The TCP maximum segment size (MSS) on IPv4 must be in the range [536 - 1460]");
                Local_TCPv4_MSS = serverConfig.TcpConfig.TCPv4_MSS;

                if (Local_TCPv6_MSS < Local_TCPv4_MSS)
                    Local_TCPv6v4_MSS = Local_TCPv6_MSS;
                else
                    Local_TCPv6v4_MSS = Local_TCPv4_MSS;

                if (!(1000 <= serverConfig.TcpConfig.MaxBufferedPackets && serverConfig.TcpConfig.MaxBufferedPackets <= 10000))
                    throw new InvalidOperationException("The pool size for TCP SocketAsyncEventArgs must be in the range [1000 - 10000]");

                if (!(1000 <= serverConfig.UdpConfig.MaxBufferedPackets && serverConfig.UdpConfig.MaxBufferedPackets <= 10000))
                    throw new InvalidOperationException("The pool size for UDP SocketAsyncEventArgs must be in the range [1000 - 10000]");

                if (!(1452 <= serverConfig.UdpConfig.PacketSize && serverConfig.UdpConfig.PacketSize <= 65535))
                    throw new InvalidOperationException("The UDP packet size must be in the range [1452 - 65535]");

                if (!(1000 <= serverConfig.MaxSessions && serverConfig.MaxSessions <= 100000))
                    throw new InvalidOperationException("The maximum connection establishing sessions must be in the range [1000 - 100000]");

                TCPSaeaPool.POOL_SIZE = serverConfig.TcpConfig.MaxBufferedPackets;
                TCPSaeaPool.Init();

                UDPSaeaPool.POOL_SIZE = serverConfig.UdpConfig.MaxBufferedPackets;
                UDPSaeaPool.PKT_SIZE = serverConfig.UdpConfig.PacketSize + 48;
                UDPSaeaPool.Init();

                CSaeaPool = new SaeaPool();
                CSaeaPool.Init(serverConfig.MaxSessions, 256);

                Softnet.ServerKit.TaskScheduler.Start();
                Softnet.ServerKit.Monitor.Start(10);

                TcpDispatcher.Init();
                UdpDispatcher.Init();

                if (LocalIPv6Bytes != null)
                {
                    IPAddress localIPv6 = new IPAddress(LocalIPv6Bytes);
                    RawTcpBindingV6.Init(localIPv6);
                    RawTcpBindingV6.Start();
                    TcpListenerV6.Start();

                    RawUdpBindingV6.Init(localIPv6);
                    RawUdpBindingV6.Start();
                    UdpListenerV6.Start();

                    s_isIPv6Bound = true;
                }
                else
                    s_isIPv6Bound = false;

                if (LocalIPv4Bytes != null)
                {
                    IPAddress localIPv4 = new IPAddress(LocalIPv4Bytes);
                    RawTcpBindingV4.Init(localIPv4);
                    RawTcpBindingV4.Start();
                    TcpListenerV4.Start();

                    RawUdpBindingV4.Init(localIPv4);
                    RawUdpBindingV4.Start();
                    UdpListenerV4.Start();

                    s_isIPv4Bound = true;
                }
                else
                    s_isIPv4Bound = false;
            }
            catch (InvalidOperationException e)
            {
                _logger.LogInformation("Service failed to start: {error}", e.Message);
                throw e;
            }
            catch (SocketException e)
            {
                _logger.LogInformation("Service failed to start: {error}", e.Message);
                throw e;
            }

            return Task.CompletedTask;
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            if (s_isIPv6Bound)
            {
                RawTcpBindingV6.Stop();
                TcpListenerV6.Stop();

                RawUdpBindingV6.Stop();
                UdpListenerV6.Stop();
            }

            if (s_isIPv4Bound)
            {
                RawTcpBindingV4.Stop();
                TcpListenerV4.Stop();

                RawUdpBindingV4.Stop();
                UdpListenerV4.Stop();
            }

            TcpDispatcher.Clear();
            UdpDispatcher.Clear();
            Softnet.ServerKit.TaskScheduler.Close();

            _logger.LogInformation("Service stopped at: {time}", DateTimeOffset.Now);

            await base.StopAsync(cancellationToken);
        }
    }
}
