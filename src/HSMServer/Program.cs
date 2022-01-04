using System;
using System.Net;
using System.Security.Authentication;
using HSMCommon.Constants;
using HSMServer.Core.Configuration;
using HSMServer.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;
using NLog.Web;

namespace HSMServer
{
    internal static class Program
    {
        private const string NLogConfigFileName = "nlog.config";


        public static void Main(string[] args)
        {
            var logger = NLogBuilder.ConfigureNLog(NLogConfigFileName).GetCurrentClassLogger();

            CertificatesConfig.InitializeConfig();

            try
            {
                logger.Debug("init main");

                var host = CreateHostBuilder(args).Build();

                StartSignalRService(host);

                host.Run();
            }
            catch (Exception ex)
            {
                logger.Fatal(ex, "Program stopped because of an exception");
                throw;
            }
            finally
            {
                NLog.LogManager.Shutdown();
            }
        }

        private static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.ConfigureKestrel(options =>
                    {
                        options.ConfigureHttpsDefaults(
                            httpsOptions => httpsOptions.ClientCertificateMode = ClientCertificateMode.RequireCertificate);

                        options.Listen(IPAddress.Any, ConfigurationConstants.SensorsPort,
                            listenOptions =>
                            {
                                listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
                                listenOptions.UseHttps(portOptions =>
                                {
                                    portOptions.CheckCertificateRevocation = false;
                                    portOptions.SslProtocols = SslProtocols.Tls13 | SslProtocols.Tls12;
                                    portOptions.ClientCertificateMode = ClientCertificateMode.NoCertificate;
                                    portOptions.ServerCertificate = CertificatesConfig.ServerCertificate;
                                });
                            });

                        options.Listen(IPAddress.Any, ConfigurationConstants.SitePort,
                            listenOptions =>
                            {
                                listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
                                listenOptions.UseHttps(portOptions =>
                                {
                                    portOptions.CheckCertificateRevocation = false;
                                    portOptions.SslProtocols = SslProtocols.Tls13 | SslProtocols.Tls12;
                                    portOptions.ClientCertificateMode = ClientCertificateMode.NoCertificate;
                                    portOptions.ServerCertificate = CertificatesConfig.ServerCertificate;
                                });
                            });

                        options.Limits.MaxRequestBodySize = 41943040;//Set up to 40 MB
                        options.Limits.MaxConcurrentConnections = 100;
                        options.Limits.MaxConcurrentUpgradedConnections = 100;
                        options.Limits.MinRequestBodyDataRate = new MinDataRate(bytesPerSecond: 100,
                            gracePeriod: TimeSpan.FromSeconds(10));
                        options.Limits.MinResponseDataRate = new MinDataRate(bytesPerSecond: 100,
                            gracePeriod: TimeSpan.FromSeconds(10));
                        options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(2);
                        options.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(1);
                    });

                    webBuilder.UseStartup<Startup>();
                })
                .ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.SetMinimumLevel(LogLevel.Trace);
                    logging.AddNLog();
                    logging.AddNLogWeb();
                })
                .UseNLog()
                .UseConsoleLifetime();

        private static void StartSignalRService(IHost host)
        {
            using var serviceScope = host.Services.CreateScope();
            var services = serviceScope.ServiceProvider;
            var serviceContext = services.GetRequiredService<IClientMonitoringService>();

            serviceContext.Initialize();
        }
    }
}
