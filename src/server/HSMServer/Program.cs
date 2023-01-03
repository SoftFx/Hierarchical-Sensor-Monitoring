using System;
using FluentValidation.AspNetCore;
using HSMCommon.Constants;
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

const string NLogConfigFileName = "nlog.config";

var builder = WebApplication.CreateBuilder(args);
var serverConfig = new ServerConfig(builder.Configuration, builder.Environment);

LayoutRenderer.Register("buildConfiguration", logEvent => builder.Environment.IsDevelopment() ? "Debug" : "Release");
LayoutRenderer.Register("infrastructureLogger", logEvent => CommonConstants.InfrastructureLoggerName);

var logger = NLogBuilder.ConfigureNLog(NLogConfigFileName).GetCurrentClassLogger();

builder.WebHost.ConfigureWebHost(serverConfig);

builder.Logging.ClearProviders().SetMinimumLevel(LogLevel.Trace).AddNLog().AddNLogWeb();
builder.Host.UseNLog().UseConsoleLifetime();


builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                .AddCookie(options => options.LoginPath = new PathString("/Account/Index"));

builder.Services.AddHsts(options =>
{
    options.Preload = true;
    options.IncludeSubDomains = true;
});
builder.Services.AddMvc();

builder.Services.AddFluentValidationAutoValidation().AddFluentValidationClientsideAdapters();

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
    
    
    app.ConfigureMiddleware(app.Environment.IsDevelopment());
    
    app.MapControllers();
    app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action}");
    app.MapControllerRoute(
        name: "Account",
        pattern: "{controller=Account}/{action}",
        defaults: new { controller = "Account" });
    app.MapControllerRoute(
        name: "Home",
        pattern: "{controller=Home}/{action=Index}");

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