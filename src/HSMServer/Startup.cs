using HSMServer.Authentication;
using HSMServer.ClientUpdateService;
using HSMServer.Configuration;
using HSMServer.DataLayer;
using HSMServer.Middleware;
using HSMServer.MonitoringServerCore;
using HSMServer.Products;
using HSMServer.SignalR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.PlatformAbstractions;
using System;
using System.IO;
using System.Linq;
using HSMServer.Handlers;
using HSMServer.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using HSMServer.Model.ViewModel;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;

namespace HSMServer
{
    public class Startup
    {
        private IServiceCollection services;
        public void ConfigureServices(IServiceCollection services)
        { 
            //services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            //    .AddJwtBearer(options =>
            //    {
            //        options.RequireHttpsMetadata = true;
            //        options.TokenValidationParameters = new TokenValidationParameters
            //        {
            //            ValidateIssuer = true,
            //            //ValidIssuer = 
            //            ValidateAudience = true,
            //            //ValidAudience = 
            //            //IssuerSigningKey = 
            //            ValidateIssuerSigningKey = true
            //        };
            //    });
            services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                .AddCookie(options =>
                {
                    options.LoginPath = new PathString("/Account/Authenticate");
                });

            services.AddHsts(options =>
            {
                options.Preload = true;
                options.IncludeSubDomains = true;
                options.MaxAge = TimeSpan.FromDays(365);
            });


            services.AddGrpc().AddServiceOptions<Services.HSMService>(options =>
            {
                options.MaxSendMessageSize = 40 * 1024 * 1024;
                options.MaxReceiveMessageSize = 40 * 1024 * 1024;
                options.EnableDetailedErrors = true;
            });
            services.AddControllers();
            services.AddControllersWithViews();
            //services.AddCors(options =>
            //    {
            //        options.AddDefaultPolicy(builder =>
            //            builder.SetIsOriginAllowed(_ => true)
            //                .AllowCredentials());
            //    });

            //services.AddAuthentication("BasicAuthentication")
            //    .AddScheme<AuthenticationSchemeOptions, BasicAuthenticationHandler>("BasicAuthentication", null);

            services.AddSignalR(hubOptions =>
            {
                hubOptions.EnableDetailedErrors = true;
            });

            //services.AddSingleton<IDatabaseClass, DatabaseClass>();
            services.AddSingleton<IDatabaseClass, LevelDBDatabaseClass>();
            services.AddSingleton<IProductManager, ProductManager>();
            services.AddSingleton<CertificateManager>();
            services.AddSingleton<IUserManager, UserManager>();
            services.AddSingleton<ISignalRSessionsManager, SignalRSessionsManager>();
            services.AddSingleton<ITreeViewManager, TreeViewManager>();
            services.AddSingleton<IConfigurationProvider, ConfigurationProvider>();
            services.AddSingleton<IBarSensorsStorage, BarSensorsStorage>();
            services.AddSingleton<IMonitoringCore, MonitoringCore>();
            services.AddSingleton<ClientCertificateValidator>();
            services.AddSingleton<IUpdateService, UpdateServiceCore>();
            services.AddSingleton<Services.HSMService>();
            services.AddSingleton<Services.AdminService>();
            services.AddSingleton<IClientMonitoringService, ClientMonitoringService>();
            services.AddScoped<IUserService, UserService>();
            //services.AddSingleton<SensorsController>();
            //services.AddSingleton<ValuesController>();

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
            });

            this.services = services;
        }       
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            var lifeTimeService = (IHostApplicationLifetime)app.ApplicationServices.GetService(typeof(IHostApplicationLifetime));
            lifeTimeService.ApplicationStopping.Register(OnShutdown, app.ApplicationServices);

            app.UseCertificateValidator();
            app.UseTokenAuth();
            //TODO: uncomment when the middleware is ready
            //app.UseBasicAuthentication();

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

            app.UseStaticFiles();
            app.UseRouting();
            app.UseCors();
            app.UseAuthorization();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapGrpcService<Services.HSMService>();
                endpoints.MapGrpcService<Services.AdminService>();

                //endpoints.MapGet("/Protos/sensors_service.proto", async context =>
                //{
                //    await context.Response.WriteAsync(
                //        await System.IO.File.ReadAllTextAsync("Protos/sensors_service.proto"));
                //});
                endpoints.MapHub<MonitoringDataHub>("/monitoring", options =>
                    {
                        options.Transports = HttpTransportType.ServerSentEvents; //only server can send messages
                    });

                endpoints.MapControllers();
                endpoints.MapControllerRoute(
                    name: "default",
                    pattern: "{controller=Home}/{action=Main}/{id?}"
                );
            });


            //app.UseForwardedHeaders(new ForwardedHeadersOptions
            //{
            //    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
            //});
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
