using System.Net;
using System.Security.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Hosting;

namespace HSMgRPC
{
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        // Additional configuration is required to successfully run gRPC on macOS.
        // For instructions on how to configure Kestrel and gRPC clients on macOS, visit https://go.microsoft.com/fwlink/?linkid=2099682
        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.ConfigureKestrel(options =>
                    {
                        options.Listen(IPAddress.Loopback, 5015, listenOptions =>
                        {
                            listenOptions.Protocols = HttpProtocols.Http2;
                        });
                        options.Listen(IPAddress.Loopback, 44313, listenOptions =>
                        {
                            listenOptions.UseHttps();
                            listenOptions.Protocols = HttpProtocols.Http1;
                            listenOptions.KestrelServerOptions.ConfigureHttpsDefaults(httpsOptions =>
                            {
                                httpsOptions.SslProtocols = SslProtocols.Tls13;
                            });
                        });
                    });
                    webBuilder.UseStartup<Startup>();
                });
    }
}
