using HSMServer.Core.Model.Authentication;
using HSMServer.Helpers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace HSMServer.Filters.ProductRoleFilters
{
    public sealed class ProductRoleFilterByEncodedProductId : ProductRoleFilterBase
    {
        protected override string ArgumentName => "encodedProductId";


        public ProductRoleFilterByEncodedProductId(params ProductRoleEnum[] roles) : base(roles) { }


        protected override string GetProductId(object arg, ActionExecutingContext _) =>
            arg is string encodedProductId ? SensorPathHelper.Decode(encodedProductId) : null;
    }
}
