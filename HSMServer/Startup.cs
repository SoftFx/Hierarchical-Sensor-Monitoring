using System;
using System.IO;
using HSMServer.DataLayer;
using HSMServer.MonitoringServerCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.PlatformAbstractions;
using NLog;

namespace HSMServer
{
    public class Startup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddHsts(options =>
            {
                options.Preload = true;
                options.IncludeSubDomains = true;
                options.MaxAge = TimeSpan.FromDays(365);
            });


            services.AddGrpc();
            services.AddControllers();

            services.AddCors();

            services.AddSingleton<DatabaseClass>();
            services.AddSingleton<MonitoringCore>();
            services.AddScoped<IMonitoringCore, MonitoringCore>();

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
        }       
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, IHostApplicationLifetime lifetime)
        {
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

            app.UseRouting();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapGrpcService<Services.SensorsService>();
              
                endpoints.MapGet("/Protos/sensors_service.proto", async context =>
                {
                    await context.Response.WriteAsync(
                        await System.IO.File.ReadAllTextAsync("Protos/sensors_service.proto"));
                });

                endpoints.MapControllers();
            });

            

            app.UseHttpsRedirection();

            app.UseCors(x => x.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());

            lifetime.ApplicationStopping.Register(OnShutdown);
        }

        public void OnShutdown()
        {
            Console.WriteLine("Stopping application");
            System.Diagnostics.Process.GetCurrentProcess().Kill();
        }
    }
}
