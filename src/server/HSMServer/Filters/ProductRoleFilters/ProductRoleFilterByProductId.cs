using HSMServer.Model.Authentication;
using Microsoft.AspNetCore.Mvc.Filters;
using System;

namespace HSMServer.Filters.ProductRoleFilters
{
    public sealed class ProductRoleFilterByProductId : ProductRoleFilterBase
    {
        protected override string ArgumentName { get; set; } = "productId";


        public ProductRoleFilterByProductId(params ProductRoleEnum[] roles) : base(roles) { }


        protected override Guid? GetEntityId(object arg, ActionExecutingContext _) =>
            arg is Guid productId ? productId : null;
    }
}
