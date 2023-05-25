using HSMServer.Model.Authentication;
using Microsoft.AspNetCore.Mvc.Filters;
using System;

namespace HSMServer.Filters.ProductRoleFilters
{
    public sealed class ProductRoleFilterByProductId : ProductRoleFilterBase
    {
        public ProductRoleFilterByProductId(string argumentName, params ProductRoleEnum[] roles) : base(argumentName, roles) { }


        protected override Guid? GetEntityId(object arg, ActionExecutingContext _) =>
            arg is Guid productId ? productId : null;
    }
}
