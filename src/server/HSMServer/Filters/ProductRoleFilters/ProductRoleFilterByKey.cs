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


        protected override Guid? GetEntityId(object arg, ActionExecutingContext _) =>
            arg is EditAccessKeyViewModel key ? key.SelectedProductId : null;
    }
}
