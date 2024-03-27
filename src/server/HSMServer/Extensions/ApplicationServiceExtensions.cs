using HSMDatabase.DatabaseWorkCore;
using HSMDataCollector.Core;
using HSMServer.Authentication;
using HSMServer.BackgroundServices;
using HSMServer.ConcurrentStorage;
using HSMServer.Core.Cache;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Journal;
using HSMServer.Core.SensorsUpdatesQueue;
using HSMServer.Core.TreeStateSnapshot;
using HSMServer.Dashboards;
using HSMServer.Filters;
using HSMServer.Folders;
using HSMServer.Middleware;
using HSMServer.Model.TreeViewModel;
using HSMServer.Notifications;
using HSMServer.ServerConfiguration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Authentication;
using System.Threading.Tasks;
using HSMServer.Services;

namespace HSMServer.ServiceExtensions;

public static class ApplicationServiceExtensions
{
    private static readonly HashSet<Type> _asyncStorageTypes = new();


    public static IServiceCollection AddApplicationServices(this IServiceCollection services, IServerConfig config)
    {
        services.AddSingleton(config);

        services.AddSingleton<IDatabaseCore, DatabaseCore>()
                .AddSingleton<ITreeStateSnapshot, TreeStateSnapshot>()
                .AddSingleton<IUpdatesQueue, UpdatesQueue>()
                .AddSingleton<ITreeValuesCache, TreeValuesCache>()
                .AddSingleton<IJournalService, JournalService>();

        services.AddAsyncStorage<IUserManager, UserManager>()
                .AddAsyncStorage<IFolderManager, FolderManager>()
                .AddAsyncStorage<ITelegramChatsManager, TelegramChatsManager>()
                .AddAsyncStorage<IDashboardManager, DashboardManager>();

        services.AddSingleton<NotificationsCenter>()
                .AddSingleton<DataCollectorWrapper>()
                .AddSingleton<TreeViewModel>();

        services.AddHostedService<TreeSnapshotService>()
                .AddHostedService<ClearDatabaseService>()
                .AddHostedService<MonitoringBackgroundService>()
                .AddHostedService<DatacollectorService>()
                .AddHostedService<NotificationsBackgroundService>()
                .AddHostedService<BackupDatabaseService>();
        
        services.AddSingleton<ClientStatistics>();
        services.AddSingleton<DatabaseSensorsSize>();
        services.AddSingleton<DatabaseSensorsStatistics>();
        
        services.AddScoped<IPermissionService, PermissionService>();
        
        services.ConfigureDataCollector();
        
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

    public static ConfigureWebHostBuilder ConfigureWebHost(this ConfigureWebHostBuilder webHostBuilder, ServerConfig config)
    {
        webHostBuilder.ConfigureKestrel(options =>
        {
            var kestrelListenAction = KestrelListenOptions(config.ServerCertificate);

            options.ListenAnyIP(config.Kestrel.SensorPort, kestrelListenAction);
            options.ListenAnyIP(config.Kestrel.SitePort, kestrelListenAction);

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
            applicationBuilder.UseDeveloperExceptionPage();
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

        applicationBuilder.UseMiddleware<TelemetryMiddleware>();
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

    public static async Task InitStorages(this IServiceProvider services)
    {
        foreach (var type in _asyncStorageTypes)
            if (services.GetService(type) is IAsyncStorage storage)
                await storage.Initialize();
    }

    private static IServiceCollection ConfigureDataCollector(this IServiceCollection services)
    {
        services.AddSingleton<CollectorOptions>(sp => {
            var cache = sp.GetService<ITreeValuesCache>();
            
            return new CollectorOptions()
            {
                AccessKey = DataCollectorWrapper.GetSelfMonitoringKey(cache),
                ClientName = "HSMServerMonitoring" // TODO: Mb create special key and hash it so no one knows it except us, then use it instead of selfmonitoring key?
            };
        });

        services.AddSingleton<IDataCollector, DataCollector>();

        return services;
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

    private static IServiceCollection AddAsyncStorage<TService, TImplementation>(this IServiceCollection services)
        where TService : class
        where TImplementation : class, TService
    {
        _asyncStorageTypes.Add(typeof(TService));

        return services.AddSingleton<TService, TImplementation>();
    }
}