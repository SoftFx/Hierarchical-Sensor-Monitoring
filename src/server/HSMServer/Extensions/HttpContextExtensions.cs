﻿using HSMServer.Middleware;
using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;

namespace HSMServer.Extensions
{
    public static class HttpContextExtensions
    {
        private const string RequestInfoKey = nameof(PublicApiRequestInfo);


        public static bool TryReadInfo(this HttpContext context, string key, out string value)
        {
            value = context.Request.Headers.TryGetValue(key, out var rawValue) ? rawValue.ToString() : null;

            return value is not null;
        }

        public static void SetPublicApiInfo(this HttpContext context, PublicApiRequestInfo info)
        {
            context.Items.Add(RequestInfoKey, info);
        }

        public static bool TryGetPublicApiInfo(this HttpContext context, out PublicApiRequestInfo info)
        {
            info = context.Items.TryGetValue(RequestInfoKey, out var rawValue) && rawValue is PublicApiRequestInfo value ? value : null;

            return info is not null;
        }

        public static Task SetAccessError(this HttpContext context, string error)
        {
            context.Response.StatusCode = 406;

            return context.Response.WriteAsync(error);
        }
    }
}