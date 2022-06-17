using HSMServer.Core.Model.Authentication;
using Microsoft.AspNetCore.Mvc.Filters;

namespace HSMServer.Filters.ProductRoleFilters
{
    public sealed class ProductRoleFilterByProductId : ProductRoleFilterBase
    {
        protected override string ArgumentName => "productId";


        public ProductRoleFilterByProductId(params ProductRoleEnum[] roles) : base(roles) { }


        protected override string GetProductId(object arg, ActionExecutingContext _) =>
            arg is string productId ? productId : null;
    }
}
