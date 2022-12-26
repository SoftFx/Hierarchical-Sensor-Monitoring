using HSMCommon.Constants;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;
using NLog.LayoutRenderers;
using NLog.Web;
using System;
using System.Net;
using System.Security.Authentication;
using HSMServer.Model;
using Microsoft.Extensions.Configuration;

namespace HSMServer
{
    internal static class Program
    {
        private const string NLogConfigFileName = "nlog.config";
        private static ServerConfig _serverConfig;

        public static void Main(string[] args)
        {
            string appMode = "Debug";
            var development = ".Development";
#if !DEBUG
            appMode = "Release";
            development = string.Empty;
#endif
            LayoutRenderer.Register("buildConfiguration", logEvent => appMode);
            LayoutRenderer.Register("infrastructureLogger", logEvent => CommonConstants.InfrastructureLoggerName);
            var logger = NLogBuilder.ConfigureNLog(NLogConfigFileName).GetCurrentClassLogger();
       
            var builder = new ConfigurationBuilder()
                .SetBasePath(ServerConfig.ConfigPath)
                .AddJsonFile($"appsettings{development}.json", optional: true, reloadOnChange: true);
            _serverConfig = new ServerConfig(builder.Build());
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

        private static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.ConfigureKestrel(options =>
                    {
                        options.ConfigureHttpsDefaults(
                            httpsOptions => httpsOptions.ClientCertificateMode = ClientCertificateMode.RequireCertificate);
                        options.Listen(IPAddress.Any, _serverConfig.Kestrel.SensorPort,
                            listenOptions =>
                            {
                                listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
                                listenOptions.UseHttps(portOptions =>
                                {
                                    portOptions.CheckCertificateRevocation = false;
                                    portOptions.SslProtocols = SslProtocols.Tls13 | SslProtocols.Tls12;
                                    portOptions.ClientCertificateMode = ClientCertificateMode.NoCertificate;
                                    portOptions.ServerCertificate = _serverConfig.ServerCertificate.Certificate;
                                });
                            });

                        options.Listen(IPAddress.Any, _serverConfig.Kestrel.SitePort,
                            listenOptions =>
                            {
                                listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
                                listenOptions.UseHttps(portOptions =>
                                {
                                    portOptions.CheckCertificateRevocation = false;
                                    portOptions.SslProtocols = SslProtocols.Tls13 | SslProtocols.Tls12;
                                    portOptions.ClientCertificateMode = ClientCertificateMode.NoCertificate;
                                    portOptions.ServerCertificate = _serverConfig.ServerCertificate.Certificate;
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
