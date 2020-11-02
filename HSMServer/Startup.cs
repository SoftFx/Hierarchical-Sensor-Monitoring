using System;
using HSMServer.DataLayer;
using HSMServer.MonitoringServerCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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
            //services.AddSingleton<CertificateManager>();
            //services.AddSingleton<ClientCertificateValidator>();
            //services.AddSingleton<MonitoringQueueManager>();

            services.AddHttpsRedirection(configureOptions =>
            {
                configureOptions.HttpsPort = 44330;
            });

            services.AddSwaggerGen();
        }       
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, IHostApplicationLifetime lifetime)
        {
            app.UseSwagger(c =>
            {
                c.SerializeAsV2 = true;
            });

            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "api");
                c.RoutePrefix = string.Empty;
            });

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseRouting();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapGrpcService<Services.SensorsService>();
              
                endpoints.MapGet("/protos/sensors_service.proto", async context =>
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

        }
    }
}
