using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;

namespace HSMServer.Core.Tests.Infrastructure
{
    // A controller test that calls an action method directly skips the filter attributes declared on
    // it, and the role filters (UserRoleFilterBase and its descendants) are such attributes: a direct
    // call would say nothing about who may reach the action. This runs the declared authorization and
    // action filters first, as MVC does, and reports the result of the one that refused.
    internal static class DeclaredFilters
    {
        /// <returns>The result a declared filter short-circuited with, or null when the action would run.</returns>
        public static IActionResult Refusal(Controller controller, string action, params object[] arguments)
        {
            var method = controller.GetType().GetMethods()
                .Single(m => m.Name == action && Matches(m.GetParameters().Select(p => p.ParameterType).ToArray(), arguments));

            var parameters = method.GetParameters();
            var actionArguments = new Dictionary<string, object>();
            for (int i = 0; i < parameters.Length; i++)
                actionArguments[parameters[i].Name] = arguments[i];

            var actionContext = new ActionContext(controller.HttpContext, new RouteData(), new ActionDescriptor());
            var filters = method.GetCustomAttributes(inherit: true).OfType<IFilterMetadata>().ToList();

            foreach (var filter in filters.OfType<IAuthorizationFilter>())
            {
                var context = new AuthorizationFilterContext(actionContext, filters);
                filter.OnAuthorization(context);

                if (context.Result is not null)
                    return context.Result;
            }

            foreach (var filter in filters.OfType<IActionFilter>())
            {
                var context = new ActionExecutingContext(actionContext, filters, actionArguments, controller);
                filter.OnActionExecuting(context);

                if (context.Result is not null)
                    return context.Result;
            }

            return null;
        }

        private static bool Matches(Type[] parameterTypes, object[] arguments)
        {
            if (parameterTypes.Length != arguments.Length)
                return false;

            for (int i = 0; i < arguments.Length; i++)
                if (arguments[i] is not null && !parameterTypes[i].IsInstanceOfType(arguments[i]))
                    return false;

            return true;
        }
    }
}
