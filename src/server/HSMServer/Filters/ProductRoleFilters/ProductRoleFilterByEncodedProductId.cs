using HSMServer.Helpers;
using HSMServer.Model.Authentication;
using Microsoft.AspNetCore.Mvc.Filters;
using System;

namespace HSMServer.Filters.ProductRoleFilters
{
    public sealed class ProductRoleFilterByEncodedProductId : ProductRoleFilterBase
    {
        protected override string ArgumentName { get; set; }


        public ProductRoleFilterByEncodedProductId(string argumentName, params ProductRoleEnum[] roles) : base(roles)
        {
            ArgumentName = argumentName; 
        }


        protected override Guid? GetEntityId(object arg, ActionExecutingContext _) =>
            arg is string encodedProductId ? SensorPathHelper.DecodeGuid(encodedProductId) : null;
    }
}
