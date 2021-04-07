using System;
using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using HSMServer.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NLog.Web;

namespace HSMServer
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var logger = NLog.Web.NLogBuilder.ConfigureNLog("nlog.config").GetCurrentClassLogger();
            Config.InitializeConfig();

            try
            {
                logger.Debug("init main");
                CreateHostBuilder(args).Build().Run();
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
                        options.Listen(IPAddress.Any, Config.GrpcPort, listenOptions =>
                        {
                            listenOptions.Protocols = HttpProtocols.Http2;
                            //listenOptions.UseHttps(Config.ServerCertificate);
                            listenOptions.UseHttps(portOptions =>
                            {
                                portOptions.ServerCertificate = Config.ServerCertificate;
                                portOptions.ClientCertificateValidation = ValidateClientCertificate;
                                portOptions.SslProtocols = SslProtocols.Tls13 | SslProtocols.Tls12;
                            });
                        });
                        options.Listen(IPAddress.Any, Config.SensorsPort, listenOptions =>
                        {
                            listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
                            listenOptions.UseHttps(portOptions =>
                            {
                                portOptions.CheckCertificateRevocation = false;
                                portOptions.SslProtocols = SslProtocols.Tls13 | SslProtocols.Tls12;
                                portOptions.ClientCertificateMode = ClientCertificateMode.NoCertificate;
                                portOptions.ServerCertificate = Config.ServerCertificate;
                            });
                        });
                    });
                    webBuilder.UseStartup<Startup>();
                })
                .ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Trace);
                }).UseNLog().UseConsoleLifetime();

        public static bool ValidateClientCertificate(X509Certificate2 certificate, X509Chain chain,
            SslPolicyErrors policyErrors)
        {
            return true;
        }
    }
}
