using System;
using HSMgRPC.DataLayer;
using Microsoft.AspNetCore.Authentication.Certificate;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HSMgRPC
{
    public class Startup
    {
        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
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

            services.AddHttpsRedirection(configureOptions =>
            {
                configureOptions.HttpsPort = 44330;
            });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, IHostApplicationLifetime lifetime)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseRouting();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapGrpcService<Services.SensorsService>();

                //endpoints.MapGet("/", async context =>
                //{
                //    await context.Response.WriteAsync("Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");
                //});
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
