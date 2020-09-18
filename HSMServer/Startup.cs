using System;
using System.Net;
using HSMServer.Authentication;
using HSMServer.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HSMServer
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;

            //Config.InitializeConfig();
            Database.DataStorage.Initialize();
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls13 | SecurityProtocolType.Tls12;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<CertificateValidationService>();
            //services.AddAuthentication(CertificateAuthenticationDefaults.AuthenticationScheme).AddCertificate(options =>
            //{
            //    options.Events = new CertificateAuthenticationEvents
            //    {
            //        OnCertificateValidated = context =>
            //        {
            //            //Debug.Print("OnCertificateValidated");
            //            var validationService =
            //                context.HttpContext.RequestServices.GetService<CertificateValidationService>();

            //            if (validationService.ValidateCertificate(context.ClientCertificate))
            //            {
            //                var claims = new[]
            //                {
            //                    new Claim(ClaimTypes.NameIdentifier,
            //                        context.ClientCertificate.Subject,
            //                        ClaimValueTypes.String,
            //                        context.Options.ClaimsIssuer),
            //                    new Claim(ClaimTypes.Name,
            //                        context.ClientCertificate.Subject,
            //                        ClaimValueTypes.String,
            //                        context.Options.ClaimsIssuer),
            //                };

            //                context.Principal = new ClaimsPrincipal(new ClaimsIdentity(claims, context.Scheme.Name));
            //                context.Success();
            //            }
            //            else
            //            {
            //                context.Fail("invalid certificate");
            //            }

            //            return Task.CompletedTask;
            //        },
            //        OnAuthenticationFailed = context =>
            //        {
            //            //Debug.Print("OnAuthenticationFailed");
            //            context.Fail("invalid certificate");
            //            return Task.CompletedTask;
            //        }
            //    };
            //});
            //services.AddAuthentication(CertificateAuthenticationDefaults.AuthenticationScheme)
            //    .AddCertificate(options =>
            //        {
            //            //options.AllowedCertificateTypes = CertificateTypes.Chained;
            //            //options.RevocationMode = X509RevocationMode.NoCheck;

            //            options.Events = new CertificateAuthenticationEvents
            //            {
            //                OnCertificateValidated = context =>
            //                {
            //                    var validationService = context.HttpContext.RequestServices
            //                        .GetService<CertificateValidationService>();
            //                    if (validationService.ValidateCertificate(context.ClientCertificate))
            //                    {
            //                        context.Success();
            //                    }
            //                    else
            //                    {
            //                        context.Fail("Invalid certificate");
            //                    }

            //                    return Task.CompletedTask;
            //                },
            //                OnAuthenticationFailed = context =>
            //                {
            //                    context.Fail("Invalid certificate");
            //                    return Task.CompletedTask;
            //                }

            //            };
            //        }
            //    );
            services.AddCors();
            services.AddControllers(config =>
                {
                    config.Filters.Add(new BasicAuthFilter());
                })
                .ConfigureApiBehaviorOptions(options =>
                    {
                        options.SuppressConsumesConstraintForFormFileParameters = true;
                        options.SuppressInferBindingSourcesForParameters = true;
                        options.SuppressModelStateInvalidFilter = true;
                        options.SuppressMapClientErrors = true;
                        options.ClientErrorMapping[StatusCodes.Status404NotFound].Link = "https://httpstatuses.com/404";
                    });

            services.AddAuthentication("BasicAuthentication")
                .AddScheme<AuthenticationSchemeOptions, BasicAuthenticationHandler>("BasicAuthentication", null);

            //services.AddAuthentication()

            services.AddScoped<IUserService, UserService>();
            services.AddScoped<BasicAuthFilter>();
            services.AddSwaggerGen();
            services.Configure<MvcOptions>(options =>
            {
                //Do not use it because of request forwarding to which some clients may not obey
                //options.Filters.Add(new RequireHttpsAttribute());
            });
            //services.AddCertificateForwarding(options =>
            //{
            //    options.CertificateHeader = "X-ARR-ClientCert";
            //    options.HeaderConverter = (headerValue) =>
            //    {
            //        X509Certificate2 clientCertificate = null;

            //        if (!string.IsNullOrWhiteSpace(headerValue))
            //        {
            //            byte[] bytes = StringToByteArray(headerValue);
            //            clientCertificate = new X509Certificate2(bytes);
            //            Debug.Print($"Certificate validation called, header {headerValue}");
            //        }
            //        return clientCertificate;
            //    };
            //});
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, IHostApplicationLifetime lifetime)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseHttpsRedirection();

            app.UseSwagger(c => { c.SerializeAsV2 = true; });

            app.UseSwaggerUI(c => 
            { 
                c.SwaggerEndpoint("swagger/v1/swagger.json", "API");
                c.RoutePrefix = string.Empty;
            });

            app.UseRouting();

            app.UseCors(x => x.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());

            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });

            app.UseAuthentication();

            //app.UseCertificateForwarding();


            lifetime.ApplicationStopping.Register(OnShutdown);
        }

        public void OnShutdown()
        {
            Database.DataStorage.DisposeDatabase();
            Config.Dispose();
        }

        private static byte[] StringToByteArray(string hex)
        {
            int NumberChars = hex.Length;
            byte[] bytes = new byte[NumberChars / 2];

            for (int i = 0; i < NumberChars; i += 2)
            {
                bytes[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
            }

            return bytes;
        }
    }
}
