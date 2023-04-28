using HSMServer.Controllers;
using HSMServer.Model.Authentication;
using Microsoft.AspNetCore.Mvc.Filters;
using System;
using System.Linq;

namespace HSMServer.Filters.ProductRoleFilters
{
    public abstract class ProductRoleFilterBase : UserRoleFilterBase
    {
        public ProductRoleFilterBase(params ProductRoleEnum[] parameters) : base(parameters) { }


        protected override bool HasRole(User user, Guid? entityId, ProductRoleEnum role) =>
            user.ProductsRoles.Any(r => r.Item1 == entityId && r.Item2 == role);

        protected override bool TryGetEntityId(ActionExecutingContext context, out Guid? productId)
        {
            var result = base.TryGetEntityId(context, out productId);

            if (result && context.Controller is AccessKeysController keysController)
            {
                var product = keysController.TreeValuesCache.GetProduct(productId.Value);
                while (product?.Parent != null)
                {
                    productId = product.Parent.Id;
                    product = product.Parent;
                }
            }

            return result;
        }
    }
}
