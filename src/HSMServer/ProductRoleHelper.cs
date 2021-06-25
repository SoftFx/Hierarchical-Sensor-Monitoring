using HSMServer.Authentication;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer
{
    public static class ProductRoleHelper
    {
        public static bool IsAvailable(string productKey,
            List<KeyValuePair<string, ProductRoleEnum>> productsRights)
        {
            var pair = productsRights?.FirstOrDefault(x => x.Key.Equals(productKey));
            if (pair.Value.Key != null) return true;

            return false;
        }

        public static bool IsViewer(string productKey,
            List<KeyValuePair<string, ProductRoleEnum>> productsRigths)
        {
            var pair = productsRigths?.FirstOrDefault(x => x.Key.Equals(productKey));
            if (pair.Value.Key != null && pair.Value.Value == ProductRoleEnum.Viewer)
                return true;

            return false;
        }

        public static bool IsManager(string productKey,
            List<KeyValuePair<string, ProductRoleEnum>> productsRights)
        {
            var pair = productsRights?.FirstOrDefault(x => x.Key.Equals(productKey));
            if (pair.Value.Key != null && pair.Value.Value == ProductRoleEnum.Manager)
                return true;

            return false;
        }
    }
}
