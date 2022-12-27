using System;
using System.IO;
using FluentValidation.AspNetCore;
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
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.PlatformAbstractions;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;

namespace HSMServer.ServiceExtensions;

public static class ApplicationServiceExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddSignalR(hubOptions => hubOptions.EnableDetailedErrors = true);
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
            ;

            var basePath = PlatformServices.Default.Application.ApplicationBasePath;
            var xmlPath = Path.Combine(basePath, "HSMSwaggerComments.xml");
            o.IncludeXmlComments(xmlPath, true);
        });

        return services;
    }
}