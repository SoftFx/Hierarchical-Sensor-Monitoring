using HSMServer.Core.Model;
using HSMServer.Helpers;
using System;
using System.Linq;

namespace HSMServer.Model.ViewModel
{
    public record ProductViewModel
    {
        public Guid Id { get; }
        public string EncodedId { get; }
        public string Key { get; }
        public string Name { get; }
        public DateTime CreationDate { get; }
        public string ManagerName { get; }

        public ProductViewModel(string manager, ProductModel product)
        {
            Id = product.Id;
            EncodedId = SensorPathHelper.EncodeGuid(product.Id);
            Key = product.AccessKeys.FirstOrDefault().Value?.Id.ToString();
            Name = product.DisplayName;
            CreationDate = product.CreationDate;
            ManagerName = manager;
        }
    }
}