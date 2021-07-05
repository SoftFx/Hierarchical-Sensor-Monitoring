using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.DataLayer.Model
{
    public class Product
    {
        public string Key { get; set; }
        public string Name { get; set; }
        public DateTime DateAdded { get; set; }
        public List<ExtraProductKey> ExtraKeys { get; set; }

        public Product() { }
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
    }
}
