using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HSMgRPC.DataLayer;
using HSMgRPC.Services;
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
            services.AddGrpc();
            services.AddControllers();

            services.AddCors();

            services.AddSingleton<DatabaseClass>();
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
                endpoints.MapControllers();

                endpoints.MapGrpcService<GreeterService>();
                endpoints.MapGrpcService<Services.SensorsService>();

                endpoints.MapGet("/", async context =>
                {
                    await context.Response.WriteAsync("Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");
                });
                endpoints.MapGet("/protos/greet.proto", async context =>
                {
                    await context.Response.WriteAsync(await System.IO.File.ReadAllTextAsync("Protos/greet.proto"));
                });
            });

            

            app.UseHttpsRedirection();

            app.UseCors(x => x.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());

            lifetime.ApplicationStopping.Register(OnShutdown);
            //app.UseAuthentication();

            //app.UseCertificateForwarding();
        }

        public void OnShutdown()
        {

        }
    }
}
