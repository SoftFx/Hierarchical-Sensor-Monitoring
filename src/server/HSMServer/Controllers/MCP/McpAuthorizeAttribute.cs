using HSMServer.Authentication;
using HSMServer.Core.Model;
using HSMServer.Model.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System;
using System.Linq;

namespace HSMServer.Controllers.MCP
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public class McpAuthorizeAttribute : Attribute, IAuthorizationFilter
    {
        public void OnAuthorization(AuthorizationFilterContext context)
        {
            var keyValue = context.HttpContext.Request.Headers["Key"].FirstOrDefault();

            if (string.IsNullOrEmpty(keyValue))
            {
                context.Result = new UnauthorizedObjectResult(new { error = "Missing Key header" });
                return;
            }

            if (!Guid.TryParse(keyValue, out var keyId))
            {
                context.Result = new UnauthorizedObjectResult(new { error = "Invalid Key format" });
                return;
            }

            var userManager = context.HttpContext.RequestServices.GetService(typeof(IUserManager)) as IUserManager;
            if (userManager == null)
            {
                context.Result = new ObjectResult(new { error = "Service unavailable" }) { StatusCode = 503 };
                return;
            }

            var mcpKey = userManager.GetMcpAccessKey(keyId);
            if (mcpKey == null)
            {
                context.Result = new UnauthorizedObjectResult(new { error = "Invalid Key" });
                return;
            }

            if (!mcpKey.IsValid(out var validationMessage))
            {
                context.Result = new UnauthorizedObjectResult(new { error = validationMessage });
                return;
            }

            var user = userManager.GetUsers(u => u.Id == mcpKey.UserId).FirstOrDefault();
            if (user == null)
            {
                context.Result = new UnauthorizedObjectResult(new { error = "User not found" });
                return;
            }

            context.HttpContext.Items["McpAccessKey"] = mcpKey;
            context.HttpContext.Items["McpAccessKeyUser"] = user;
        }
    }

    public static class McpAuthorizationExtensions
    {
        public static McpAccessKeyModel GetMcpAccessKey(this Microsoft.AspNetCore.Http.HttpContext context) =>
            context.Items.TryGetValue("McpAccessKey", out var key) ? key as McpAccessKeyModel : null;

        public static User GetMcpUser(this Microsoft.AspNetCore.Http.HttpContext context) =>
            context.Items.TryGetValue("McpAccessKeyUser", out var user) ? user as User : null;
    }
}
