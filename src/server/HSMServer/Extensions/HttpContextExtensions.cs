using HSMServer.Middleware;
using Microsoft.AspNetCore.Http;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HSMServer.Extensions
{
    public static class HttpContextExtensions
    {
        private const string RequestInfoKey = nameof(PublicApiRequestInfo);
        private const string RequestFaultKey = "PublicApiRequestFault";


        public static bool TryReadInfo(this HttpContext context, string key, out string value)
        {
            value = context.Request.Headers.TryGetValue(key, out var rawValue) ? rawValue.ToString() : null;

            return value is not null;
        }

        public static bool TryWriteInfo<T>(this HttpContext context, string key, T value) =>
            context.Request.Headers.TryAdd(key, value.ToString());

        public static void SetPublicApiInfo(this HttpContext context, PublicApiRequestInfo info)
        {
            context.Items.Add(RequestInfoKey, info);
        }

        public static bool TryGetPublicApiInfo(this HttpContext context, out PublicApiRequestInfo info)
        {
            info = context.Items.TryGetValue(RequestInfoKey, out var rawValue) && rawValue is PublicApiRequestInfo value ? value : null;

            return info is not null;
        }

        // The caller of the request could not be resolved because the server itself failed
        // while looking the key up (#1756): not a refusal of the key. The value routes do
        // not ask and answer the access error as before; the registration routes answer 500.
        public static void SetPublicApiFault(this HttpContext context) => context.Items[RequestFaultKey] = true;

        public static bool HasPublicApiFault(this HttpContext context) => context.Items.ContainsKey(RequestFaultKey);

        public static Task SetAccessError(this HttpContext context, string error)
        {
            context.Response.StatusCode = 406;

            return context.Response.WriteAsync(error);
        }
    }
}