using HSMServer.Helpers;
using HSMServer.Model.AccessKeysViewModels;
using HSMServer.Model.Authentication;
using Microsoft.AspNetCore.Mvc.Filters;
using System;

namespace HSMServer.Filters.ProductRoleFilters
{
    public class ProductRoleFilterByKey : ProductRoleFilterBase
    {
        protected override string ArgumentName => "key";


        public ProductRoleFilterByKey(params ProductRoleEnum[] roles) : base(roles) { }


        protected override Guid? GetProductId(object arg, ActionExecutingContext _) =>
            arg is EditAccessKeyViewModel key ? SensorPathHelper.DecodeGuid(key.SelectedProductId) : null;
    }
}
