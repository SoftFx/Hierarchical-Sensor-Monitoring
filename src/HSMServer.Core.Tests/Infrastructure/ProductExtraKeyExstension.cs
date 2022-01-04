using HSMServer.Core.Keys;
using HSMServer.Core.Model;

namespace HSMServer.Core.Tests.Infrastructure
{
    internal static class ProductExtraKeyExstension
    {
        internal static void AddExtraKey(this Product product, string extraKeyName)
        {
            var extraKey = new ExtraProductKey
            {
                Name = extraKeyName,
                Key = KeyGenerator.GenerateExtraProductKey(product.Name, extraKeyName)
            };
            product.ExtraKeys.Add(extraKey);
        }
    }
}
