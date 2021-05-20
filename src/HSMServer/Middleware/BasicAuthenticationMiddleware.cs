using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HSMServer.Constants;
using Microsoft.AspNetCore.Http;

namespace HSMServer.Middleware
{
    public class BasicAuthenticationMiddleware
    {
        private readonly RequestDelegate _next;
        //private readonly User_Hub_Manager

        public BasicAuthenticationMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var port = context.Connection.LocalPort;
            if (port == ConfigurationConstants.ApiPort)
            {
                var basicAuthHeader = context.Request.Headers["Authorization"];
                string credentialsString = Encoding.ASCII.GetString(Convert.FromBase64String(basicAuthHeader.ToString()));

                
            }

            await _next.Invoke(context);
        }
    }
}
