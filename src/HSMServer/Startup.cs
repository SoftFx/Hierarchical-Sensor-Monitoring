using FluentValidation.AspNetCore;
using HSMDatabase.DatabaseInterface;
using HSMDatabase.DatabaseWorkCore;
using HSMServer.BackgroundTask;
using HSMServer.Core.Authentication;
using HSMServer.Core.Configuration;
using HSMServer.Core.DataLayer;
using HSMServer.Core.MonitoringHistoryProcessor.Factory;
using HSMServer.Core.MonitoringServerCore;
using HSMServer.Core.Products;
using HSMServer.Core.Registration;
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
using System;
using System.IO;
using System.Linq;
using HSMServer.Core.Cache;

namespace HSMServer
{
    public class Startup
    {
        private IServiceCollection services;
        public void ConfigureServices(IServiceCollection services)
        { 
            services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                .AddCookie(options =>
                {
                    options.LoginPath = new PathString("/Account/Index");
                });

            services.AddHsts(options =>
            {
                options.Preload = true;
                options.IncludeSubDomains = true;
                options.MaxAge = TimeSpan.FromDays(365);
            });

            services.AddMvc().AddFluentValidation();

            //services.AddGrpc().AddServiceOptions<Services.HSMService>(options =>
            //{
            //    options.MaxSendMessageSize = 40 * 1024 * 1024;
            //    options.MaxReceiveMessageSize = 40 * 1024 * 1024;
            //    options.EnableDetailedErrors = true;
            //});
            services.AddControllers();
            services.AddControllersWithViews();

            services.AddSignalR(hubOptions =>
            {
                hubOptions.EnableDetailedErrors = true;
            });

            //services.AddSingleton<IDatabaseWorker, LevelDBDatabaseWorker>();
            //services.AddTransient<IPublicAdapter, PublicAdapter>();
            services.AddTransient<IHistoryProcessorFactory, HistoryProcessorFactory>();
            //Use singleton, created in DatabaseCore
            //services.AddSingleton<IDatabaseCore, DatabaseCore>();
            services.AddTransient<IDatabaseAdapter, DatabaseAdapter>();
            services.AddSingleton<IConverter, Converter>();
            services.AddSingleton<IProductManager, ProductManager>();
            services.AddSingleton<CertificateManager>();
            services.AddSingleton<IUserManager, UserManager>();
            services.AddSingleton<IRegistrationTicketManager, RegistrationTicketManager>();
            services.AddSingleton<ISignalRSessionsManager, SignalRSessionsManager>();
            services.AddSingleton<ITreeViewManager, TreeViewManager>();
            services.AddSingleton<IConfigurationProvider, ConfigurationProvider>();
            services.AddSingleton<IBarSensorsStorage, BarSensorsStorage>();
            services.AddSingleton<IValuesCache, ValuesCache>();
            services.AddSingleton<IMonitoringCore, MonitoringCore>();
            //services.AddSingleton<ClientCertificateValidator>();
            //services.AddSingleton<IUpdateService, UpdateServiceCore>();
            //services.AddSingleton<Services.HSMService>();
            //services.AddSingleton<AdminService>();
            services.AddSingleton<IClientMonitoringService, ClientMonitoringService>();


            services.AddHostedService<OutdatedSensorService>();

            services.AddHttpsRedirection(configureOptions =>
            {
                configureOptions.HttpsPort = 44330;
            });

            services.AddSwaggerGen();

            services.ConfigureSwaggerGen(options =>
            {
                var basePath = PlatformServices.Default.Application.ApplicationBasePath;
                var xmlPath = Path.Combine(basePath, "HSMServer.xml");
                options.IncludeXmlComments(xmlPath, true);
                options.DocumentFilter<SwaggerIgnoreFilter>();
            });

            this.services = services;
        }       
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            //env.EnvironmentName = "Production";
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

            //app.UseCertificateValidator();


            app.UseAuthentication();
            app.UseSwagger(c =>
            {
                //c.RouteTemplate = "api/swagger/swagger/{documentName}/swagger.json";
                c.SerializeAsV2 = true;
            });

            app.UseSwaggerUI(c =>
            {
                c.RoutePrefix = "api/swagger";
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "HSM server api");
            });

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

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
                //endpoints.MapGrpcService<Services.HSMService>();
                //endpoints.MapGrpcService<Services.AdminService>();

                endpoints.MapHub<MonitoringDataHub>("/monitoring", options =>
                    {
                        options.Transports = HttpTransportType.ServerSentEvents; //only server can send messages
                    });

                endpoints.MapControllers();
                endpoints.MapControllerRoute(
                    name: "default",
                    pattern: "{controller=Home}/{action}"
                );
                endpoints.MapControllerRoute(
                    name: "Account",
                    pattern: "{controller=Account}/{action}",
                    defaults: new { controller = "Account"}
                );
                endpoints.MapControllerRoute(
                    name: "Home",
                    pattern: "{controller=Home}/{action=Index}"
                );
            });

            app.UseHttpsRedirection();
        }

        public void OnShutdown(object state)
        {
            var serviceProvider = (IServiceProvider) state;
            var objectToDispose = services
                .Where(s => s.Lifetime == ServiceLifetime.Singleton
                            && s.ImplementationInstance != null
                            && s.ServiceType.GetInterfaces().Contains(typeof(IMonitoringCore)))
                .Select(s => s.ImplementationInstance as IMonitoringCore).First();

            objectToDispose.Dispose();
            System.Diagnostics.Process.GetCurrentProcess().Kill();
        }
    }
}
