using HSMServer.Core.Cache.Entities;
using HSMServer.Helpers;
using System;

namespace HSMServer.Model.TreeViewModels
{
    public class AccessKeyViewModel
    {
        public Guid Id { get; }

        public string ProductId { get; }

        public string EncodedProductId { get; set; }

        public string Name { get; set; }


        // public constructor without parameters for action Home/AddAccessKey with [FromBody]AccessKeyViewModel
        public AccessKeyViewModel() { }

        internal AccessKeyViewModel(AccessKeyModel accessKey)
        {
            Id = accessKey.Id;
            ProductId = accessKey.ProductId;
            Name = accessKey.DisplayName;
        }


        internal AccessKeyModel ToModel(Guid userId) =>
            new(userId.ToString(), SensorPathHelper.Decode(EncodedProductId))
            {
                DisplayName = Name,
            };
    }
}
