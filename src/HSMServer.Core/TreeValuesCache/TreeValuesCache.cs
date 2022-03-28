using HSMDatabase.DatabaseWorkCore;
using HSMServer.Core.DataLayer;
using HSMServer.Core.TreeValuesCache.Entities;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HSMServer.Core.TreeValuesCache
{
    public interface ITreeValuesCache
    {
        List<ProductValue> GetTree();
    }

    public sealed class TreeValuesCache : ITreeValuesCache
    {
        private readonly ConcurrentDictionary<Guid, ProductValue> _tree;


        public TreeValuesCache(IDatabaseAdapter database)
        {
            _tree = new ConcurrentDictionary<Guid, ProductValue>();

            var products = new List<ProductValue>();

            var productEntities = database.GetAllProducts();
            foreach (var productEntity in productEntities)
            {
                var product = new ProductValue(productEntity);
                //var productSensors = database.GetProductSensors(product.Id);
                //product.Sensors.AddRange(productSensors);
                products.Add(product);
            }

            foreach (var product in products)
                _tree.TryAdd(product.Id, product);
        }


        public List<ProductValue> GetTree() => _tree.Values.ToList();
    }
}
