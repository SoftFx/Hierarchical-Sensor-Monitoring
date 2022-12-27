using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using System;
using System.Net;
using System.Security.Authentication;
using FluentValidation.AspNetCore;
using HSMCommon.Constants;
using HSMServer.Middleware;
using HSMServer.Model;
using HSMServer.ServiceExtensions;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;
using NLog.LayoutRenderers;
using NLog.Web;

const string nLogConfigFileName = "nlog.config";
var builder = WebApplication.CreateBuilder(args);

var serverConfig = new ServerConfig(builder.Configuration);
LayoutRenderer.Register("buildConfiguration", logEvent => builder.Environment.IsDevelopment() ? "Debug" : "Release");
LayoutRenderer.Register("infrastructureLogger", logEvent => CommonConstants.InfrastructureLoggerName);

var logger = NLogBuilder.ConfigureNLog(nLogConfigFileName).GetCurrentClassLogger();

builder.WebHost.ConfigureKestrel(options =>
{
    options.ConfigureHttpsDefaults(
        httpsOptions => httpsOptions.ClientCertificateMode = ClientCertificateMode.RequireCertificate);
    options.Listen(IPAddress.Any, serverConfig.Kestrel.SensorPort,
        listenOptions =>
        {
            listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
            listenOptions.UseHttps(portOptions =>
            {
                portOptions.CheckCertificateRevocation = false;
                portOptions.SslProtocols = SslProtocols.Tls13 | SslProtocols.Tls12;
                portOptions.ClientCertificateMode = ClientCertificateMode.NoCertificate;
                portOptions.ServerCertificate = serverConfig.ServerCertificate.Certificate;
            });
        });

    options.Listen(IPAddress.Any, serverConfig.Kestrel.SitePort,
        listenOptions =>
        {
            listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
            listenOptions.UseHttps(portOptions =>
            {
                portOptions.CheckCertificateRevocation = false;
                portOptions.SslProtocols = SslProtocols.Tls13 | SslProtocols.Tls12;
                portOptions.ClientCertificateMode = ClientCertificateMode.NoCertificate;
                portOptions.ServerCertificate = serverConfig.ServerCertificate.Certificate;
            });
        });

    options.Limits.MaxRequestBodySize = 52428800; // Set up to ~50MB
    options.Limits.MaxConcurrentConnections = 100;
    options.Limits.MaxConcurrentUpgradedConnections = 100;
    options.Limits.MinRequestBodyDataRate = null;
    options.Limits.MinResponseDataRate = null;
    options.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(1);
});

builder.Host.ConfigureLogging(logging =>
    {
        logging.ClearProviders();
        logging.SetMinimumLevel(LogLevel.Trace);
        logging.AddNLog();
        logging.AddNLogWeb();
    })
    .UseNLog()
    .UseConsoleLifetime();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options => options.LoginPath = new PathString("/Account/Index"));

builder.Services.AddHsts(options =>
{
    options.Preload = true;
    options.IncludeSubDomains = true;
    options.MaxAge = TimeSpan.FromDays(365);
});
builder.Services.AddMvc();

builder.Services.AddFluentValidation(options =>
{
    options.ImplicitlyValidateChildProperties = true;
    options.ImplicitlyValidateRootCollectionElements = true;
});

builder.Services.AddHttpsRedirection(configureOptions => configureOptions.HttpsPort = 44330);

builder.Services.AddApplicationServices();

try
{
    var app = builder.Build();

    if (app.Environment.IsDevelopment())
    {
        app.UseDeveloperExceptionPage();
    }
    else
    {
        app.UseExceptionHandler("/Error");
    }

    app.UseAuthentication();
    app.CountRequestStatistics();
    app.UseMiddleware<LoggingExceptionMiddleware>();

    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.RoutePrefix = "api/swagger";
        c.SwaggerEndpoint($"/swagger/{ServerConfig.Version}/swagger.json", "HSM server api");
    });

    app.UseStaticFiles(new StaticFileOptions
    {
        OnPrepareResponse = context =>
            context.Context.Response.Headers.Add("Cache-control", "no-cache")
    });
    app.UseRouting();
    app.UseCors();
    app.UseAuthorization();
    app.UseUserProcessor();
    app.UseEndpoints(endpoints =>
    {
        endpoints.MapControllers();
        endpoints.MapControllerRoute(
            name: "default",
            pattern: "{controller=Home}/{action}"
        );
        endpoints.MapControllerRoute(
            name: "Account",
            pattern: "{controller=Account}/{action}",
            defaults: new { controller = "Account" }
        );
        endpoints.MapControllerRoute(
            name: "Home",
            pattern: "{controller=Home}/{action=Index}"
        );
    });

    app.UseHttpsRedirection();
    app.Run();
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