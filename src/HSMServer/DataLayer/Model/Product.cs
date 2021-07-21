using System;
using System.Collections.Generic;
using System.Linq;
using HSMDatabase.Entity;

namespace HSMServer.DataLayer.Model
{
    public class Product
    {
        public string Key { get; set; }
        public string Name { get; set; }
        public DateTime DateAdded { get; set; }
        public List<ExtraProductKey> ExtraKeys { get; set; }
        public Product() { }
        public Product(string key, string name, DateTime dateAdded)
        {
            Key = key;
            Name = name;
            DateAdded = dateAdded;
        }
        public Product(Product product)
        {
            if (product == null) return;

            Key = product.Key;
            Name = product.Name;
            DateAdded = product.DateAdded;
            ExtraKeys = new List<ExtraProductKey>();
            if (product.ExtraKeys != null && product.ExtraKeys.Any())
                ExtraKeys.AddRange(product.ExtraKeys);
        }

        public Product(ProductEntity entity)
        {
            if (entity == null) return;

            Key = entity.Key;
            Name = entity.Name;
            DateAdded = entity.DateAdded;
            ExtraKeys = new List<ExtraProductKey>();
            if (entity.ExtraKeys != null && entity.ExtraKeys.Any())
            {
                ExtraKeys.AddRange(entity.ExtraKeys.Select(e => new ExtraProductKey(e)));
            }
        }

        public void Update(Product product)
        {
            ExtraKeys = new List<ExtraProductKey>();
            if (product.ExtraKeys != null && product.ExtraKeys.Any())
            {
                ExtraKeys.AddRange(product.ExtraKeys);
            }
        }
    }
}
