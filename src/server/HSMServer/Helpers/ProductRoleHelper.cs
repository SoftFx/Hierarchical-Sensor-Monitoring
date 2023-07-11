using HSMServer.Model.Authentication;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Helpers
{
    public static class ProductRoleHelper
    {
        public static bool IsProductActionAllowed(List<(Guid, ProductRoleEnum)> productsRights)
        {
            return productsRights.FirstOrDefault(x => x.Item2 == ProductRoleEnum.ProductManager).Item1 != Guid.Empty;
        }

        public static bool IsManager(Guid productId, List<(Guid, ProductRoleEnum)> productsRights)
        {
            var pair = productsRights?.FirstOrDefault(x => x.Item1.Equals(productId));
            if (pair != null && pair.Value.Item1 != Guid.Empty && pair.Value.Item2 == ProductRoleEnum.ProductManager)
                return true;

            return false;
        }
    }
}
