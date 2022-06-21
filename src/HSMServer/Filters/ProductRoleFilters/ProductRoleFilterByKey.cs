using HSMServer.Core.Model.Authentication;
using HSMServer.Helpers;
using HSMServer.Model.AccessKeysViewModels;
using Microsoft.AspNetCore.Mvc.Filters;

namespace HSMServer.Filters.ProductRoleFilters
{
    public class ProductRoleFilterByKey : ProductRoleFilterBase
    {
        protected override string ArgumentName => "key";


        public ProductRoleFilterByKey(params ProductRoleEnum[] roles) : base(roles) { }


        protected override string GetProductId(object arg, ActionExecutingContext _) =>
            arg is EditAccessKeyViewModel key ? SensorPathHelper.Decode(key.EncodedProductId) : null;
    }
}
