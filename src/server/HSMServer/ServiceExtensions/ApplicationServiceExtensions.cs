using HSM.Core.Monitoring;
using HSMDatabase.DatabaseWorkCore;
using HSMServer.Authentication;
using HSMServer.BackgroundTask;
using HSMServer.Configuration;
using HSMServer.Core.Cache;
using HSMServer.Core.DataLayer;
using HSMServer.Core.SensorsUpdatesQueue;
using HSMServer.Filters;
using HSMServer.Folders;
using HSMServer.Middleware;
using HSMServer.Model;
using HSMServer.Model.TreeViewModel;
using HSMServer.Notifications;
using HSMServer.Registration;
using HSMServer.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using System;
using System.IO;
using System.Security.Authentication;

namespace HSMServer.ServiceExtensions;

public static class ApplicationServiceExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddSingleton<IDatabaseCore, DatabaseCore>();
        services.AddSingleton<IUpdatesQueue, UpdatesQueue>();
        services.AddSingleton<ITreeValuesCache, TreeValuesCache>();
        services.AddSingleton<IUserManager, UserManager>();
        services.AddSingleton<IFolderManager, FolderManager>();

        services.AddSingleton<IRegistrationTicketManager, RegistrationTicketManager>();
        services.AddSingleton<IConfigurationProvider, ConfigurationProvider>();

        services.AddSingleton<NotificationsCenter>();
        services.AddSingleton<DataCollectorWrapper>();
        services.AddSingleton<TreeViewModel>();

        services.AddHostedService<OutdatedSensorService>();
        services.AddHostedService<DatacollectorService>();
        services.AddHostedService<DatacollectorService>();

        services.AddSwaggerGen(o =>
        {
            o.UseInlineDefinitionsForEnums();
            o.OperationFilter<DataRequestHeaderSwaggerFilter>();
            o.SwaggerDoc(ServerConfig.Version.ToString(), new OpenApiInfo
            {
                Version = ServerConfig.Version.ToString(),
                Title = ServerConfig.Name,
            });

            o.MapType<TimeSpan>(() => new OpenApiSchema
            {
                Type = "string",
                Example = new OpenApiString("00.00:00:00")
            });

            o.MapType<Version>(() => new OpenApiSchema
            {
                Type = "string",
                Example = new OpenApiString("0.0.0.0")
            });

            var xmlPath = Path.Combine(Environment.CurrentDirectory, "HSMSwaggerComments.xml");
            o.IncludeXmlComments(xmlPath, true);

            o.TagActionsBy(api =>
            {
                if (api.GroupName != null)
                    return new[] { api.GroupName };

                if (api.ActionDescriptor is ControllerActionDescriptor controllerActionDescriptor)
                    return new[] { controllerActionDescriptor.ControllerName };

                throw new InvalidOperationException("Unable to determine tag for endpoint.");
            });

            o.DocInclusionPredicate((name, api) => true); //for controllers groupping
        });

        return services;
    }

    public static ConfigureWebHostBuilder ConfigureWebHost(this ConfigureWebHostBuilder webHostBuilder, ServerConfig serverConfig)
    {
        webHostBuilder.ConfigureKestrel(options =>
        {
            var kestrelListenAction = KestrelListenOptions(serverConfig.ServerCertificate);

            options.ListenAnyIP(serverConfig.Kestrel.SensorPort, kestrelListenAction);
            options.ListenAnyIP(serverConfig.Kestrel.SitePort, kestrelListenAction);

            options.Limits.MaxRequestBodySize = 52428800; // Set up to ~50MB
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
            applicationBuilder.UseDeveloperExceptionPage();
        }
        else
        {
            applicationBuilder.UseHsts();
            applicationBuilder.UseExceptionHandler("/Error");
        }

        applicationBuilder.UseHttpsRedirection();

        applicationBuilder.UseStaticFiles();

        applicationBuilder.UseRouting();

        applicationBuilder.UseAuthentication();
        applicationBuilder.UseAuthorization();

        applicationBuilder.UseMiddleware<RequestStatisticsMiddleware>();
        applicationBuilder.UseMiddleware<UserProcessorMiddleware>();
        applicationBuilder.UseMiddleware<LoggingExceptionMiddleware>();

        applicationBuilder.UseSwagger();
        applicationBuilder.UseSwaggerUI(c =>
        {
            c.RoutePrefix = "api/swagger";
            c.SwaggerEndpoint($"/swagger/{ServerConfig.Version}/swagger.json", "HSM server api");
        });

        return applicationBuilder;
    }

    private static Action<ListenOptions> KestrelListenOptions(ServerCertificateConfig config) =>
        options =>
        {
            options.Protocols = HttpProtocols.Http1AndHttp2;
            options.UseHttps(portOptions =>
            {
                portOptions.SslProtocols = SslProtocols.Tls13 | SslProtocols.Tls12;
                portOptions.ServerCertificate = config.Certificate;
            });
        };
}