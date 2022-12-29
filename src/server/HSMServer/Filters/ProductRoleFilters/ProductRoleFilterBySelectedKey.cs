using HSMServer.Controllers;
using HSMServer.Core.Model.Authentication;
using Microsoft.AspNetCore.Mvc.Filters;
using System;

namespace HSMServer.Filters.ProductRoleFilters
{
    public class ProductRoleFilterBySelectedKey : ProductRoleFilterBase
    {
        protected override string ArgumentName => "selectedKey";


        public ProductRoleFilterBySelectedKey(params ProductRoleEnum[] roles) : base(roles) { }


        protected override Guid? GetProductId(object arg, ActionExecutingContext context)
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
