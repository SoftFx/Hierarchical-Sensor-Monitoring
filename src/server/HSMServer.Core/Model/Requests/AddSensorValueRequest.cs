using System;


namespace HSMServer.Core.Model.Requests
{
    public sealed record AddSensorValueRequest : BaseUpdateRequest
    {
        public BaseValue BaseValue { get; init; }

        public Guid Key { get; init; }

        public AddSensorValueRequest(Guid productId, string path, BaseValue value) : base (productId, path)
        {
            BaseValue = value;
        }
    }
}