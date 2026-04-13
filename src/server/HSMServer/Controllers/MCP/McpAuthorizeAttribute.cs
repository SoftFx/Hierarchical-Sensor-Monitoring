using HSMServer.Core.Cache;
using HSMServer.Core.Model;
using HSMServer.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System;

namespace HSMServer.Controllers.MCP
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public class McpAuthorizeAttribute : Attribute, IAuthorizationFilter
    {
        public bool HealthOnly { get; set; }

        public void OnAuthorization(AuthorizationFilterContext context)
        {
            if (HealthOnly && !context.HttpContext.TryGetPublicApiInfo(out _))
            {
                return;
            }

            if (!context.HttpContext.TryGetPublicApiInfo(out var info))
            {
                context.Result = new UnauthorizedObjectResult(new { error = "Invalid or missing access key. Provide 'Key' header with valid GUID." });
                return;
            }

            if (info.Key.State == KeyState.Blocked)
            {
                context.Result = new ObjectResult(new { error = "Access key is blocked" }) { StatusCode = 403 };
                return;
            }

            if (info.Key.State == KeyState.Expired || info.Key.ExpirationTime < DateTime.UtcNow)
            {
                context.Result = new ObjectResult(new { error = "Access key has expired" }) { StatusCode = 403 };
                return;
            }
        }
    }

    public static class McpAuthorizationExtensions
    {
        public static Guid? GetKeyProductId(this AuthorizationFilterContext context)
        {
            if (context.HttpContext.TryGetPublicApiInfo(out var info))
                return info.Product.Id;
            return null;
        }

        public static Guid? GetKeyId(this AuthorizationFilterContext context)
        {
            if (context.HttpContext.TryGetPublicApiInfo(out var info))
                return info.Key.Id;
            return null;
        }
    }
}
