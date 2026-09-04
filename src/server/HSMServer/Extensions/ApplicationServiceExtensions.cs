using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Authentication;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using HSMDatabase.AccessManager;
using HSMDatabase.DatabaseWorkCore;
using HSMDatabase.Settings;
using HSMServer.Authentication;
using HSMServer.BackgroundServices;
using HSMServer.ConcurrentStorage;
using HSMServer.Core.Cache;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Journal;
using HSMServer.Core.TreeStateSnapshot;
using HSMServer.Dashboards;
using HSMServer.Filters;
using HSMServer.Folders;
using HSMServer.Middleware;
using HSMServer.Middleware.Telemetry;
using HSMServer.Migrations;
using HSMServer.Model.TreeViewModel;
using HSMServer.Notifications;
using HSMServer.Notifications.Chats;
using HSMServer.ServerConfiguration;
using HSMServer.Core.Schedule;


namespace HSMServer.ServiceExtensions
{

    public static class ApplicationServiceExtensions
    {
        private static readonly HashSet<Type> _asyncStorageTypes = [];


        public static IServiceCollection AddApplicationServices(this IServiceCollection services, IServerConfig config)
        {
            services.AddSingleton(config);

            services.AddSingleton<IDatabaseCore, DatabaseCore>()
                    .AddSingleton<IDatabaseSettings, DatabaseSettings>()
                    .AddSingleton<ChatMigrator>()
                    .AddSingleton<IAlertScheduleProvider, AlertScheduleProvider>()
                    .AddSingleton<ITreeStateSnapshot, TreeStateSnapshot>()
                    .AddSingleton<ITreeValuesCache, TreeValuesCache>()
                    .AddSingleton<IJournalService, JournalService>();

            services.AddAsyncStorage<IUserManager, UserManager>()
                    .AddAsyncStorage<IFolderManager, FolderManager>()
                    .AddAsyncStorage<IChatsManager, ChatsManager>()
                    .AddAsyncStorage<IDashboardManager, DashboardManager>()
                    .AddAsyncStorage<IApiTokenManager, ApiTokenManager>();

            // Management-API authorization foundation (#1356 step 3): append-only
            // per-request security events and the effective-rights/resource evaluator.
            services.AddSingleton<IApiTokenSecurityEventSink, ApiTokenSecurityEventSink>();
            services.AddSingleton<IApiTokenAuthorizationService, ApiTokenAuthorizationService>();

            // Retention + abuse bounds (#1356, prerequisite of the step-4 management
            // endpoints): bounded cleanup of dead token rows/orphans/security events and
            // the per-source budget for failed-authentication events.
            services.AddSingleton(config.ApiTokens);
            services.AddSingleton<ApiTokenInvalidAttemptLimiter>();
            services.AddSingleton<ApiTokenRetentionCleaner>();

            services.AddSingleton<DataCollectorWrapper>()
                    .AddSingleton<TreeViewModel>()
                    .AddSingleton<TelemetryCollector>()
                    .AddSingleton<BackupDatabaseService>()
                    .AddSingleton<NotificationsCenter>()
                    .AddSingleton<ChatSensorUsageCalculator>();

            services.AddHttpClient<SlackNotificationChannel>();
            services.AddHttpClient<MattermostNotificationChannel>();

            services.AddSingleton<HSMServer.Core.Restore.IRestoreService, HSMServer.Core.Restore.RestoreService>();

            services.AddHostedService<TreeSnapshotService>()
                    .AddHostedService<ClearDatabaseService>()
                    //                .AddHostedService<MonitoringBackgroundService>()
                    .AddHostedService<DataCollectorService>()
                    .AddHostedService<NotificationsBackgroundService>()
                    .AddHostedService<BackgroundServices.DatabaseServices.RestoreTempCleanupService>()
                    .AddHostedService<BackgroundServices.DatabaseServices.ApiTokenRetentionService>()
                    .AddHostedService(provider => provider.GetService<BackupDatabaseService>());

            services.AddSwaggerGen(o =>
            {
                o.UseInlineDefinitionsForEnums();
                o.OperationFilter<DataRequestHeaderSwaggerFilter>();
                o.OperationFilter<ManagementApiSecuritySwaggerFilter>();
                o.SwaggerDoc(ServerConfig.Version, new OpenApiInfo
                {
                    Version = ServerConfig.Version,
                    Title = ServerConfig.Name,
                    // The self-describing entry point for /api/v1 clients (#1353): an
                    // agent that reads only this document can authenticate and work
                    // through the management area.
                    Description =
                        "HSM server API. The /api/v1 management area serves non-interactive clients " +
                        "(AI agents, scripts): every operation requires the HsmApiToken bearer credential " +
                        "(see the HsmApiToken security scheme), and every error response — 400/401/403/404/409/500 — " +
                        "carries the uniform JSON body {\"error\": <machine-readable code>, \"message\": <human summary>, " +
                        "\"details\": <field-keyed messages on 400s, traceId on 500s, else null>}. " +
                        "Management endpoints are served on the web-UI port only.",
                });

                o.AddSecurityDefinition(ManagementApiSecuritySwaggerFilter.SchemeName, new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "hsm_pat_v1_<token id>.<secret>",
                    Description =
                        "Personal API token of the management API. The full credential is minted by the server " +
                        "and disclosed to its owner exactly once; provisioning is an operator action (a self-service " +
                        "issuance flow is a follow-up). Send it verbatim as the Authorization header value: " +
                        "'Authorization: Bearer hsm_pat_v1_...'. A token acts with the intersection of its own grants " +
                        "and its owner's current rights; missing, revoked or malformed credentials all answer the same " +
                        "generic 401.",
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
                        return [api.GroupName];

                    if (api.ActionDescriptor is ControllerActionDescriptor controllerActionDescriptor)
                        return [controllerActionDescriptor.ControllerName];

                    throw new InvalidOperationException("Unable to determine tag for endpoint.");
                });

                o.DocInclusionPredicate((name, api) => true); //for controllers groupping
            });

            return services;
        }

        public static ConfigureWebHostBuilder ConfigureWebHost(this ConfigureWebHostBuilder webHostBuilder, ServerConfig config)
        {
            // One immutable registry both drives the actual Listen calls and answers
            // IsSitePort for the /api/v1 area guard: the guard can never disagree with
            // what is listening, and a config change takes effect only on restart.
            var listeners = new HsmListenerBindings(config.Kestrel.SitePort, config.Kestrel.SensorPort);

            webHostBuilder.ConfigureServices(services => services.AddSingleton(listeners));

            webHostBuilder.ConfigureKestrel(options =>
            {
                var kestrelListenAction = KestrelListenOptions(config.ServerCertificate);

                options.Listen(IPAddress.Any, listeners.SensorPort, kestrelListenAction);
                options.Listen(IPAddress.Any, listeners.SitePort, kestrelListenAction);

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

            // Between the global handler and the logging middleware (the inner one logs
            // first, then rethrows): /api paths answer their 500s with the uniform JSON
            // error contract instead of the Razor /Error page (#1353) — never HTML on
            // /api. Everything else rethrows untouched for the global handler.
            applicationBuilder.UseMiddleware<ApiExceptionJsonMiddleware>();

            applicationBuilder.UseMiddleware<LoggingExceptionMiddleware>();

            applicationBuilder.UseHttpsRedirection();

            applicationBuilder.UseStaticFiles();

            applicationBuilder.UseRouting();

            // Management-area isolation (#1356 step 3), before authentication: the area
            // guard allow-lists /api/v1 per endpoint (marker + policy) and SitePort-only
            // before any controller runs, and the bearer guard keeps an hsm_pat_ credential
            // off every legacy route with a plain non-redirecting 401.
            applicationBuilder.UseMiddleware<ManagementApiGuardMiddleware>();
            applicationBuilder.UseMiddleware<LegacyBearerGuardMiddleware>();

            applicationBuilder.UseAuthentication();
            applicationBuilder.UseAuthorization();

            applicationBuilder.UseMiddleware<TelemetryMiddleware>();
            applicationBuilder.UseMiddleware<UserProcessorMiddleware>();


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
            services.GetRequiredService<ChatMigrator>().Migrate(services.GetRequiredService<IDatabaseCore>());

            foreach (var type in _asyncStorageTypes)
                if (services.GetService(type) is IAsyncStorage storage)
                    await storage.Initialize();
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
}