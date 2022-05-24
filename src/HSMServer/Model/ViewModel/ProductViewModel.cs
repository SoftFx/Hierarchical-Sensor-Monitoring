using HSMServer.Core.Cache.Entities;
using System;
using System.Linq;

namespace HSMServer.Model.ViewModel
{
    public record ProductViewModel
    {
        public string Id { get; }
        public string Key { get; }
        public string Name { get; }
        public DateTime CreationDate { get; }
        public string ManagerName { get; }

        public ProductViewModel(string manager, ProductModel product)
        {
            Id = product.Id;
            Key = product.AccessKeys.First().Value.Id.ToString();
            Name = product.DisplayName;
            CreationDate = product.CreationDate;
            ManagerName = manager;
        }
    }
}