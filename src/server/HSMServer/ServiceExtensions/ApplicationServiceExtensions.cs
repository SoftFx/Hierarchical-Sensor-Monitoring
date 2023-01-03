using System;
using System.IO;
using System.Security.Authentication;
using HSM.Core.Monitoring;
using HSMDatabase.DatabaseWorkCore;
using HSMServer.BackgroundTask;
using HSMServer.Core.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Configuration;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Registration;
using HSMServer.Core.SensorsUpdatesQueue;
using HSMServer.Filters;
using HSMServer.Model;
using HSMServer.Model.TreeViewModels;
using HSMServer.Notifications;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.PlatformAbstractions;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using HSMServer.Middleware;

namespace HSMServer.ServiceExtensions;

public static class ApplicationServiceExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddSingleton<IDatabaseCore, DatabaseCore>();
        services.AddSingleton<IUserManager, UserManager>();
        services.AddSingleton<IRegistrationTicketManager, RegistrationTicketManager>();
        services.AddSingleton<IConfigurationProvider, ConfigurationProvider>();
        services.AddSingleton<IUpdatesQueue, UpdatesQueue>();
        services.AddSingleton<ITreeValuesCache, TreeValuesCache>();
        services.AddSingleton<INotificationsCenter, NotificationsCenter>();
        services.AddSingleton<IDataCollectorFacade, DataCollectorFacade>();
        services.AddSingleton<TreeViewModel>();

        services.AddHostedService<OutdatedSensorService>();
        services.AddHostedService<DatabaseMonitoringService>();
        services.AddHostedService<MonitoringBackgroundService>();
        
        services.AddSwaggerGen(o =>
        {
            o.UseInlineDefinitionsForEnums();
            o.OperationFilter<DataRequestHeaderSwaggerFilter>();
            o.SwaggerDoc(ServerConfig.Version, new OpenApiInfo
            {
                Version = ServerConfig.Version,
                Title = ServerConfig.Name,
            });
            o.MapType<TimeSpan>(() => new OpenApiSchema
            {
                Type = "string",
                Example = new OpenApiString("00.00:00:00")
            });

            var basePath = PlatformServices.Default.Application.ApplicationBasePath;
            var xmlPath = Path.Combine(basePath, "HSMSwaggerComments.xml");
            o.IncludeXmlComments(xmlPath, true);
        });
        
        return services;
    }

    public static ConfigureWebHostBuilder ConfigureWebHost(this ConfigureWebHostBuilder webHostBuilder, ServerConfig serverConfig)
    {
        webHostBuilder.ConfigureKestrel(options =>
        {
            options.ListenAnyIP(serverConfig.Kestrel.SensorPort,
                listenOptions =>
                {
                    listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
                    listenOptions.UseHttps(portOptions =>
                    {
                        portOptions.SslProtocols = SslProtocols.Tls13 | SslProtocols.Tls12;
                        portOptions.ServerCertificate = serverConfig.ServerCertificate.Certificate;
                    });
                });
            
            options.ListenAnyIP(serverConfig.Kestrel.SitePort, listenOptions =>
            {
                listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
                listenOptions.UseHttps(portOptions =>
                {
                    portOptions.SslProtocols = SslProtocols.Tls13 | SslProtocols.Tls12;
                    portOptions.ServerCertificate = serverConfig.ServerCertificate.Certificate;
                });
            });

            options.Limits.MaxRequestBodySize = 52428800; // Set up to ~50MB
            options.Limits.MaxConcurrentConnections = 100;
            options.Limits.MaxConcurrentUpgradedConnections = 100;
            options.Limits.MinRequestBodyDataRate = null; //???
            options.Limits.MinResponseDataRate = null; // ???
            options.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(1);
        });
        return webHostBuilder;
    }

    public static IApplicationBuilder ConfigureMiddleware(this IApplicationBuilder applicationBuilder, bool isDevelopment)
    {
        if (isDevelopment)
        {
            applicationBuilder.UseHsts();
            applicationBuilder.UseDeveloperExceptionPage();
        }
        else
        {
            applicationBuilder.UseExceptionHandler("/Error");
        }

        applicationBuilder.UseHttpsRedirection();
        
        applicationBuilder.UseStaticFiles();
        
        applicationBuilder.UseRouting();
        
        applicationBuilder.UseAuthentication();
        applicationBuilder.UseAuthorization();
        
        applicationBuilder.CountRequestStatistics();
        applicationBuilder.UseMiddleware<LoggingExceptionMiddleware>();
        applicationBuilder.UseUserProcessor();
        
        applicationBuilder.UseSwagger();
        applicationBuilder.UseSwaggerUI(c =>
        {
            c.RoutePrefix = "api/swagger";
            c.SwaggerEndpoint($"/swagger/{ServerConfig.Version}/swagger.json", "HSM server api");
        });
        
        return applicationBuilder;
    }
}