using HSMServer.DataLayer.Model;
using System;

namespace HSMServer.Model.ViewModel
{
    public class ProductViewModel
    {
        public string Key { get; set; }
        public string Name { get; set; }
        public DateTime DateAdded { get; set; }

        public ProductViewModel(Product product)
        {
            Key = product.Key;
            Name = product.Name;
            DateAdded = product.DateAdded;
        }
    }
}
