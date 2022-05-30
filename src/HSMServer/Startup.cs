using FluentValidation.AspNetCore;
using HSM.Core.Monitoring;
using HSMServer.BackgroundTask;
using HSMServer.Certificates;
using HSMServer.Core.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Configuration;
using HSMServer.Core.DataLayer;
using HSMServer.Core.MonitoringCoreInterface;
using HSMServer.Core.MonitoringHistoryProcessor.Factory;
using HSMServer.Core.MonitoringServerCore;
using HSMServer.Core.Registration;
using HSMServer.Core.SensorsUpdatesQueue;
using HSMServer.Filters;
using HSMServer.Middleware;
using HSMServer.Model.TreeViewModels;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.PlatformAbstractions;
using System;
using System.IO;

namespace HSMServer
{
    internal sealed class Startup
    {
        private IServiceCollection _services;


        public void ConfigureServices(IServiceCollection services)
        {
            services
                .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                .AddCookie(options => options.LoginPath = new PathString("/Account/Index"));

            services.AddHsts(options =>
            {
                options.Preload = true;
                options.IncludeSubDomains = true;
                options.MaxAge = TimeSpan.FromDays(365);
            });

            services.AddMvc();
            services.AddFluentValidation();
            services.AddControllers();
            services.AddControllersWithViews();

            services.AddSignalR(hubOptions => hubOptions.EnableDetailedErrors = true);

            services.AddTransient<IHistoryProcessorFactory, HistoryProcessorFactory>();

            services.AddSingleton<IDatabaseCore>(x => CertificatesConfig.DatabaseCore);
            services.AddSingleton<IUserManager, UserManager>();
            services.AddSingleton<IRegistrationTicketManager, RegistrationTicketManager>();
            services.AddSingleton<IConfigurationProvider, ConfigurationProvider>();
            services.AddSingleton<IBarSensorsStorage, BarSensorsStorage>();
            services.AddSingleton<IDataCollectorFacade, DataCollectorFacade>();
            services.AddSingleton<IUpdatesQueue, UpdatesQueue>();
            services.AddSingleton<ITreeValuesCache, TreeValuesCache>();
            services.AddSingleton<TreeViewModel>();
            services.AddSingleton<MonitoringCore>();
            services.AddSingleton<ISensorsInterface>(x => x.GetRequiredService<MonitoringCore>());

            services.AddHostedService<OutdatedSensorService>();
            services.AddHostedService<DatabaseMonitoringService>();

            services.AddHttpsRedirection(configureOptions => configureOptions.HttpsPort = 44330);

            services.AddSwaggerGen();

            services.ConfigureSwaggerGen(options =>
            {
                var basePath = PlatformServices.Default.Application.ApplicationBasePath;
                var xmlPath = Path.Combine(basePath, "HSMSwaggerComments.xml");
                options.IncludeXmlComments(xmlPath, true);
                options.DocumentFilter<SwaggerIgnoreFilter>();
            });

            _services = services;
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

            var lifeTimeService = (IHostApplicationLifetime)app.ApplicationServices.GetService(typeof(IHostApplicationLifetime));
            lifeTimeService?.ApplicationStopping.Register(OnShutdown, app.ApplicationServices.GetService<MonitoringCore>());

            app.UseAuthentication();
            app.CountRequestStatistics();
            app.UseSwagger(c => c.SerializeAsV2 = true);

            app.UseSwaggerUI(c =>
            {
                c.RoutePrefix = "api/swagger";
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "HSM server api");
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

        private void OnShutdown(object service)
        {
            if (service is MonitoringCore monitoringCore)
                monitoringCore.Dispose();

            // TODO!!! Remove this process Kill
            System.Diagnostics.Process.GetCurrentProcess().Kill();
        }
    }
}
