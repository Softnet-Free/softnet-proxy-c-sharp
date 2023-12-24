using Microsoft.Extensions.Logging.Configuration;
using Microsoft.Extensions.Logging.EventLog;
using Softnet.Proxy.Config;

namespace Softnet.Proxy
{
    public class Program
    {
        public static void Main(string[] args)
        {
            IHost host = Host.CreateDefaultBuilder(args)                
                .ConfigureServices((hostContext, services) =>
                {
                    services.Configure<ProxyServerConfig>(hostContext.Configuration.GetSection("ProxyServer"));
                    services.AddHostedService<WindowsBackgroundService>();
                })
                .UseWindowsService()
                .Build();

            host.Run();
        }
    }
}