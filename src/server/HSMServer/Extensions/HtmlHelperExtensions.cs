﻿using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HSMServer.Extensions
{
    public static class HtmlHelperExtensions
    {
        private const string NavLinkActiveClass = "active";
        private const string ControllerKey = "controller";


        public static string ActiveClass(this IHtmlHelper htmlHelper, string controller = null) =>
            (htmlHelper?.ViewContext.RouteData.Values.TryGetValue(ControllerKey, out var currentController) ?? false) &&
            currentController is string controllerStr && controllerStr.Equals(controller, StringComparison.InvariantCultureIgnoreCase)
                ? NavLinkActiveClass
                : string.Empty;

        public static IHtmlContent MultiItemsHeader(this IHtmlHelper _, IList<string> items, int maxCount = 2, string separator = ", ", bool useLineBreak = false)
        {
            var builder = new StringBuilder();

            for (int i = 0; i < Math.Min(maxCount, items.Count); i++)
            {
                if (i > 0)
                {
                    builder.Append(separator);

                    if (useLineBreak)
                        builder.Append("<br>");
                }

                builder.Append(items[i].Trim());
            }

            if (items.Count - maxCount > 0)
            {
                if (useLineBreak)
                    builder.Append("<br>");

                builder.Append($"... and other {items.Count - maxCount}");
            }

            return new HtmlString(builder.ToString());
        }

        public static IEnumerable<SelectListItem> GetEnumSelectListWithDefaultValue<TEnum>(this IHtmlHelper htmlHelper, TEnum defaultValue)
        where TEnum : struct
        {
            var selectList = htmlHelper.GetEnumSelectList<TEnum>().ToList();
            selectList.Single(x => x.Value == $"{(int)(object)defaultValue}").Selected = true;
            return selectList;
        }
    }
}
