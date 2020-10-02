using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Grpc.Core;
using HSMgRPC.DataLayer;
using HSMServer.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SensorsService;
using SensorsService = HSMgRPC.Services.SensorsService;

namespace HSMgRPC
{
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }
        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.ConfigureKestrel(options =>
                    {
                        options.ConfigureHttpsDefaults(httpsOptions =>
                        {
                            httpsOptions.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
                        });
                        options.Listen(IPAddress.Loopback, 5015, listenOptions =>
                        {
                            listenOptions.Protocols = HttpProtocols.Http2;
                            listenOptions.UseHttps(Config.ServerCertificate);
                        });
                        options.Listen(IPAddress.Any, 44330, listenOptions =>
                        {
                            listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
                            listenOptions.UseHttps(portOptions =>
                            {
                                portOptions.CheckCertificateRevocation = false;
                                portOptions.SslProtocols = SslProtocols.Tls13 | SslProtocols.Tls11 | SslProtocols.Tls12;
                                portOptions.ClientCertificateMode = ClientCertificateMode.NoCertificate;
                            });
                        });
                        options.Listen(IPAddress.Loopback, 5020, listenOptions =>
                        {
                            listenOptions.UseHttps();
                            listenOptions.Protocols = HttpProtocols.Http2;
                            listenOptions.KestrelServerOptions.ConfigureHttpsDefaults(kestrelOptions =>
                            {
                                kestrelOptions.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
                            });
                        });
                    });
                    webBuilder.UseStartup<Startup>();
                });

        public static bool ValidateClientCertificate(X509Certificate2 certificate, X509Chain chain,
            SslPolicyErrors policyErrors)
        {
            return true;
        }
    }
}
