using HSMServer.Core.Model.Authentication;
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

        public static bool IsAvailable(string productKey,
            List<KeyValuePair<string, ProductRoleEnum>> productsRights)
        {
            var pair = productsRights?.FirstOrDefault(x => x.Key.Equals(productKey));
            if (pair.Value.Key != null) return true;

            return false;
        }

        public static bool IsViewer(string productKey,
            List<KeyValuePair<string, ProductRoleEnum>> productsRights)
        {
            var pair = productsRights?.FirstOrDefault(x => x.Key.Equals(productKey));
            if (pair.Value.Key != null && pair.Value.Value == ProductRoleEnum.ProductViewer)
                return true;

            return false;
        }

        public static bool IsManager(string productKey,
            List<KeyValuePair<string, ProductRoleEnum>> productsRights)
        {
            var pair = productsRights?.FirstOrDefault(x => x.Key.Equals(productKey));
            if (pair != null && pair.Value.Key != null && pair.Value.Value == ProductRoleEnum.ProductManager)
                return true;

            return false;
        }
    }
}
