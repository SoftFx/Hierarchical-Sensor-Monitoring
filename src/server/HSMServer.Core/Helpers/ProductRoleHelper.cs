using HSMServer.Core.Model.Authentication;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.Helpers
{
    public static class ProductRoleHelper
    {
        public static bool IsProductActionAllowed(List<KeyValuePair<string, ProductRoleEnum>> productsRights)
        {
            return productsRights.FirstOrDefault(x => x.Value == ProductRoleEnum.ProductManager).Key != null;
        }

        public static bool IsManager(Guid productId,
            List<KeyValuePair<string, ProductRoleEnum>> productsRights)
        {
            var pair = productsRights?.FirstOrDefault(x => x.Key.Equals(productId.ToString()));
            if (pair != null && pair.Value.Key != null && pair.Value.Value == ProductRoleEnum.ProductManager)
                return true;

            return false;
        }
    }
}
