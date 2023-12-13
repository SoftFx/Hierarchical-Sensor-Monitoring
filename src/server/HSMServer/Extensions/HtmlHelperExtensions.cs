using Microsoft.AspNetCore.Mvc.Rendering;
using System;

namespace HSMServer.Extensions
{
    public static class HtmlHelperExtensions
    {
        private const string ControllerKey = "controller";
        private const string NavLinkActiveClass = "active";


        public static string ActiveClass(this IHtmlHelper htmlHelper, string controller = null) =>
            (htmlHelper?.ViewContext.RouteData.Values.TryGetValue(ControllerKey, out var currentController) ?? false) &&
            currentController is string controllerStr && controllerStr.Equals(controller, StringComparison.InvariantCultureIgnoreCase)
                ? NavLinkActiveClass
                : string.Empty;
    }
}
