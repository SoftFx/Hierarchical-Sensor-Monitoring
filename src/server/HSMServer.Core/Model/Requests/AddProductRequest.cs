using System;
using HSMServer.Core.SensorsUpdatesQueue;


namespace HSMServer.Core.Model.Requests
{
    internal sealed record AddProductRequest : IUpdateRequest
    {
        public ProductModel ProductModel { get; set; }


        public AddProductRequest(string name, Guid autorId)
        {
            ProductModel = new ProductModel(name, autorId);
        }
    }
}
