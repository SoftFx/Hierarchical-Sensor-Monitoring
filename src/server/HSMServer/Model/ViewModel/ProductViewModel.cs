using HSMServer.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using HSMServer.Extensions;
using HSMServer.Model.TreeViewModels;

namespace HSMServer.Model.ViewModel
{
    public record ProductViewModel
    {
        public Guid Id { get; }

        public string EncodedId { get; }

        public string Key { get; }

        public string Name { get; }

        public string ShortLastUpdateTime { get; }

        public DateTime CreationDate { get; }

        public DateTime LastUpdateDate { get; }

        public List<string> Managers { get; }

        public ProductViewModel(List<string> managers, ProductNodeViewModel product)
        {
            Id = product.Id;
            EncodedId = SensorPathHelper.EncodeGuid(product.Id);
            Key = product.AccessKeys.FirstOrDefault().Value?.Id.ToString();
            Name = product.Name;
            LastUpdateDate = product.UpdateTime;
            ShortLastUpdateTime = LastUpdateDate.GetStaticTimeAgo();
            Managers = managers;
        }
    }
}