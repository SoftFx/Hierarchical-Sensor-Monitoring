using HSMServer.Core.Cache.Entities;
using System;

namespace HSMServer.Model.ViewModel
{
    public class ProductViewModel
    {
        public string Key { get; }
        public string Name { get; }
        public DateTime CreationDate { get; }
        public string ManagerName { get; }

        public ProductViewModel(string manager, ProductModel product)
        {
            Key = product.Id;
            Name = product.DisplayName;
            CreationDate = product.CreationDate;
            ManagerName = manager;
        }
    }
}