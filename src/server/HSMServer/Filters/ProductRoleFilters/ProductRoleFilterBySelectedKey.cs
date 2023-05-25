using HSMServer.Controllers;
using HSMServer.Model.Authentication;
using Microsoft.AspNetCore.Mvc.Filters;
using System;

namespace HSMServer.Filters.ProductRoleFilters
{
    public class ProductRoleFilterBySelectedKey : ProductRoleFilterBase
    {
        public ProductRoleFilterBySelectedKey(string argumentName, params ProductRoleEnum[] roles) : base(argumentName, roles) { }


        protected override Guid? GetEntityId(object arg, ActionExecutingContext context)
        {
            if (arg is string selectedKey)
            {
                var accessKey = (context.Controller as AccessKeysController).TreeValuesCache.GetAccessKey(Guid.Parse(selectedKey));
                return accessKey.ProductId;
            }

            return null;
        }
    }
}
