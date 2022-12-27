using FluentValidation.AspNetCore;
using HSM.Core.Monitoring;
using HSMServer.BackgroundTask;
using HSMServer.Core.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Configuration;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Registration;
using HSMServer.Core.SensorsUpdatesQueue;
using HSMServer.Filters;
using HSMServer.Middleware;
using HSMServer.Model;
using HSMServer.Model.TreeViewModels;
using HSMServer.Notifications;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.PlatformAbstractions;
using Microsoft.OpenApi.Models;
using System;
using System.IO;
using HSMDatabase.DatabaseWorkCore;
using Microsoft.OpenApi.Any;

namespace HSMServer
{
    internal sealed class Startup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                    .AddCookie(options => options.LoginPath = new PathString("/Account/Index"));

            services.AddHsts(options =>
            {
                options.Preload = true;
                options.IncludeSubDomains = true;
                options.MaxAge = TimeSpan.FromDays(365);
            });

            services.AddMvc();
            services.AddFluentValidation(options =>
            {
                options.ImplicitlyValidateChildProperties = true;
                options.ImplicitlyValidateRootCollectionElements = true;
            });

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

            services.AddHttpsRedirection(configureOptions => configureOptions.HttpsPort = 44330);

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
                });;

                var basePath = PlatformServices.Default.Application.ApplicationBasePath;
                var xmlPath = Path.Combine(basePath, "HSMSwaggerComments.xml");
                o.IncludeXmlComments(xmlPath, true);
            });
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
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
        }
    }
}
