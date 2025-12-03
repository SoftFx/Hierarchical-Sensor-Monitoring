using FluentValidation.AspNetCore;
using HSMCommon.Constants;
using HSMServer.Authentication;
using HSMServer.Middleware;
using HSMServer.ServerConfiguration;
using HSMServer.ServiceExtensions;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NLog;
using NLog.Extensions.Logging;
using NLog.LayoutRenderers;
using NLog.Web;
using System;
using System.Globalization;
using System.Reflection;
using System.Text.Json.Serialization;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;


const string NLogConfigFileName = "nlog.config";

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

config.SetBasePath(ServerConfig.ConfigPath)
      .AddJsonFile(ServerConfig.ConfigName, true, reloadOnChange: true);

var serverConfig = new ServerConfig(config);

LayoutRenderer.Register("buildConfiguration", logEvent => builder.Environment.IsDevelopment() ? "Debug" : "Release");
LayoutRenderer.Register("infrastructureLogger", logEvent => CommonConstants.InfrastructureLoggerName);

NLogBuilder.ConfigureNLog(NLogConfigFileName);

var logger = LogManager.GetLogger(CommonConstants.InfrastructureLoggerName);

var assembly = Assembly.GetExecutingAssembly();
var version = assembly.GetName().Version?.ToString();

logger.Info($"HSM Server {version} starting...");

builder.WebHost.ConfigureWebHost(serverConfig);

builder.Logging.ClearProviders()
               .SetMinimumLevel(LogLevel.Trace)
               .AddNLog()
               .AddNLogWeb();

builder.Host.UseNLog()
            .UseConsoleLifetime();


builder.Services.AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme)
                .Configure<IUserManager>((options, context) =>
                {
                    options.LoginPath = new PathString("/Account/Index");
                    options.Events = new MyCookieAuthenticationEvents(context);
                });

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme);

builder.Services.AddHsts(options =>
{
    options.Preload = true;
    options.IncludeSubDomains = true;
});

builder.Services.AddMvc()
                .AddJsonOptions(options =>
                {
                    options.JsonSerializerOptions.NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.AllowNamedFloatingPointLiterals;
                });

builder.Services.AddFluentValidationAutoValidation()
                .AddFluentValidationClientsideAdapters();

builder.Services.AddHttpsRedirection(с => с.HttpsPort = serverConfig.Kestrel.SitePort);

builder.Services.AddApplicationServices(serverConfig)
                .Configure<MonitoringOptions>(config.GetSection(nameof(serverConfig.MonitoringOptions)));

builder.Services.Configure<HostOptions>(hostOptions =>
{
    hostOptions.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore;
});

try
{
    var app = builder.Build();

    var cultureInfo = CultureInfo.InvariantCulture;

    CultureInfo.DefaultThreadCurrentCulture = cultureInfo;
    CultureInfo.DefaultThreadCurrentUICulture = cultureInfo;

    app.UseRequestLocalization(new RequestLocalizationOptions
    {
        DefaultRequestCulture = new Microsoft.AspNetCore.Localization.RequestCulture(cultureInfo),
        SupportedCultures = new[] { cultureInfo },
        SupportedUICultures = new[] { cultureInfo }
    });

    await app.Services.InitStorages();

    app.ConfigureMiddleware(app.Environment.IsDevelopment());

    app.MapControllers();

    app.MapControllerRoute(
        name: "Account",
        pattern: "{controller=Account}/{action}",
        defaults: new { controller = "Account" });

    app.MapControllerRoute(
        name: "Home",
        pattern: "{controller=Home}/{action=Index}");

    app.MapControllerRoute(
        name: "DashboardsPanelEdit",
        pattern: "{controller=Dashboards}/{dashboardId?}/{panelId?}");

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