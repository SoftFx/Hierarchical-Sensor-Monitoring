using HSMCommon.Constants;
using HSMServer.Certificates;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;
using NLog.LayoutRenderers;
using NLog.Web;
using System;
using System.IO;
using System.Net;
using System.Security.Authentication;
using Microsoft.Extensions.Configuration;

namespace HSMServer
{
    internal static class Program
    {
        private const string NLogConfigFileName = "nlog.config";


        public static void Main(string[] args)
        {
            string appMode = "Debug";
#if !DEBUG
            appMode = "Release";
#endif

            LayoutRenderer.Register("buildConfiguration", logEvent => appMode);
            LayoutRenderer.Register("infrastructureLogger", logEvent => CommonConstants.InfrastructureLoggerName);
            var logger = NLogBuilder.ConfigureNLog(NLogConfigFileName).GetCurrentClassLogger();
            
            var development = appMode == "Debug" ? ".Development" : String.Empty;
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile($"appsettings{development}.json", optional: true, reloadOnChange: true);
            var configurationRoot = builder.Build();
            
            var certificate = ((configurationRoot.GetSection("Certificate:Name").Value), configurationRoot.GetSection("Certificate:Key").Value);
            int.TryParse(configurationRoot.GetSection("SensorPort").Value, out var sensorPort);
            int.TryParse(configurationRoot.GetSection("SitePort").Value, out var sitePort);
            
            CertificatesConfig.InitializeConfig();
            try
            {
                logger.Debug("init main");

                var host = CreateHostBuilder(args).Build();

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

        private static IHostBuilder CreateHostBuilder(string[] args, (string, string) certificate, int sensorPOrt, int sitePort) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.ConfigureKestrel(options =>
                    {
                        options.ConfigureHttpsDefaults(
                            httpsOptions => httpsOptions.ClientCertificateMode = ClientCertificateMode.RequireCertificate);

                        options.Listen(IPAddress.Any, sensorPOrt,
                            listenOptions =>
                            {
                                listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
                                listenOptions.UseHttps(portOptions =>
                                {
                                    portOptions.CheckCertificateRevocation = false;
                                    portOptions.SslProtocols = SslProtocols.Tls13 | SslProtocols.Tls12;
                                    portOptions.ClientCertificateMode = ClientCertificateMode.NoCertificate;
                                    portOptions.ServerCertificate = new (certificate.Item1, certificate.Item2);
                                });
                            });

                        options.Listen(IPAddress.Any, sitePort,
                            listenOptions =>
                            {
                                listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
                                listenOptions.UseHttps(portOptions =>
                                {
                                    portOptions.CheckCertificateRevocation = false;
                                    portOptions.SslProtocols = SslProtocols.Tls13 | SslProtocols.Tls12;
                                    portOptions.ClientCertificateMode = ClientCertificateMode.NoCertificate;
                                    portOptions.ServerCertificate = new (certificate.Item1, certificate.Item2);
                                });
                            });

                        options.Limits.MaxRequestBodySize = 52428800; // Set up to ~50MB
                        options.Limits.MaxConcurrentConnections = 100;
                        options.Limits.MaxConcurrentUpgradedConnections = 100;
                        options.Limits.MinRequestBodyDataRate = null;
                        options.Limits.MinResponseDataRate = null;
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
    }
}
