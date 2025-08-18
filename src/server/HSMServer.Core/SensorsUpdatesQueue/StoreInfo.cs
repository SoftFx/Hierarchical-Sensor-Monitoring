using System;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Requests;

namespace HSMServer.Core.SensorsUpdatesQueue
{
    public sealed class StoreInfo : BaseRequestModel
    {
        public BaseValue BaseValue { get; init; }

        public ProductModel Product { get; init; }


        public StoreInfo(string key, string path) : base(key, path) { }

        public StoreInfo(Guid key, string path) : base(key, path) { }
    }
}