﻿using System;
using System.IO;
using System.Linq;
using FluentValidation.AspNetCore;
using HSM.Core.Monitoring;
using HSMServer.BackgroundTask;
using HSMServer.Core.Authentication;
using HSMServer.Core.Authentication.UserObserver;
using HSMServer.Core.Cache;
using HSMServer.Core.Configuration;
using HSMServer.Core.MonitoringCoreInterface;
using HSMServer.Core.MonitoringHistoryProcessor.Factory;
using HSMServer.Core.MonitoringServerCore;
using HSMServer.Core.Products;
using HSMServer.Core.Registration;
using HSMServer.Core.SensorsDataProcessor;
using HSMServer.Core.SensorsDataValidation;
using HSMServer.Filters;
using HSMServer.Middleware;
using HSMServer.Model.ViewModel;
using HSMServer.Services;
using HSMServer.SignalR;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.PlatformAbstractions;

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
            services.AddSingleton(CertificatesConfig.DatabaseAdapter);
            services.AddSingleton<IProductManager, ProductManager>();
            services.AddSingleton<CertificateManager>();
            services.AddSingleton<IUserManager, UserManager>();
            services.AddSingleton<IUserObservable>(x => x.GetRequiredService<IUserManager>());
            services.AddSingleton<IRegistrationTicketManager, RegistrationTicketManager>();
            services.AddSingleton<ISignalRSessionsManager, SignalRSessionsManager>();
            services.AddSingleton<IConfigurationProvider, ConfigurationProvider>();
            services.AddSingleton<ISensorsDataValidator, SensorsDataValidator>();
            services.AddSingleton<ISensorsProcessor, SensorsProcessor>();
            services.AddSingleton<IBarSensorsStorage, BarSensorsStorage>();
            services.AddSingleton<IValuesCache, ValuesCache>();
            services.AddSingleton<IDataCollectorFacade, DataCollectorFacade>();
            services.AddSingleton<MonitoringCore>();
            services.AddSingleton<IMonitoringCore>(x => x.GetRequiredService<MonitoringCore>());
            services.AddSingleton<IMonitoringDataReceiver>(x => x.GetRequiredService<MonitoringCore>());
            services.AddSingleton<IProductsInterface>(x => x.GetRequiredService<MonitoringCore>());
            services.AddSingleton<ISensorsInterface>(x => x.GetRequiredService<MonitoringCore>());
            services.AddSingleton<IMonitoringUpdatesReceiver>(x => x.GetRequiredService<MonitoringCore>());
            services.AddSingleton<ITreeViewManager, TreeViewManager>();
            services.AddSingleton<IClientMonitoringService, ClientMonitoringService>();

            services.AddHostedService<OutdatedSensorService>();
            services.AddHostedService<DatabaseMonitoringService>();
            services.AddHostedService<SensorsExpirationService>();

            services.AddHttpsRedirection(configureOptions => configureOptions.HttpsPort = 44330);

            services.AddSwaggerGen();

            services.ConfigureSwaggerGen(options =>
            {
                var basePath = PlatformServices.Default.Application.ApplicationBasePath;
                var xmlPath = Path.Combine(basePath, "HSMServer.xml");
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
            lifeTimeService?.ApplicationStopping.Register(OnShutdown, app.ApplicationServices);

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
                endpoints.MapHub<MonitoringDataHub>("/monitoring",
                    options => options.Transports = HttpTransportType.ServerSentEvents); //only server can send messages

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

        private void OnShutdown(object state)
        {
            var serviceProvider = (IServiceProvider)state;
            var objectToDispose = _services
                .Where(s => s.Lifetime == ServiceLifetime.Singleton
                            && s.ImplementationInstance != null
                            && s.ServiceType.GetInterfaces().Contains(typeof(IMonitoringCore)))
                .Select(s => s.ImplementationInstance as IMonitoringCore).First();

            objectToDispose.Dispose();
            System.Diagnostics.Process.GetCurrentProcess().Kill();
        }
    }
}
