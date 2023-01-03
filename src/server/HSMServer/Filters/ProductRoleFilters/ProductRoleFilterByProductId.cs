using HSMServer.Model.Authentication;
using Microsoft.AspNetCore.Mvc.Filters;
using System;

namespace HSMServer.Filters.ProductRoleFilters
{
    public sealed class ProductRoleFilterByProductId : ProductRoleFilterBase
    {
        protected override string ArgumentName => "productId";


        public ProductRoleFilterByProductId(params ProductRoleEnum[] roles) : base(roles) { }


        protected override Guid? GetProductId(object arg, ActionExecutingContext _) =>
            arg is Guid productId ? productId : null;
    }
}
