using HSMServer.Core.Model;
using System;

namespace HSMServer.Model.ViewModel
{
    public class ProductViewModel
    {
        public string Key { get; set; }
        public string Name { get; set; }
        public DateTime CreationDate { get; set; }
        public string ManagerName { get; set; }

        public ProductViewModel(string manager, Product product)
        {
            Key = product.Key;
            Name = product.Name;
            CreationDate = product.DateAdded;
            ManagerName = manager;
        }
    }
}