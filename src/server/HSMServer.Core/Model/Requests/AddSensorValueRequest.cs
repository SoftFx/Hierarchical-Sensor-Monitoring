using System;


namespace HSMServer.Core.Model.Requests
{
    public sealed class AddSensorValueRequest : BaseUpdateRequest
    {
        public BaseValue BaseValue { get; init; }

        public Guid Key { get; init; }

        public AddSensorValueRequest(string productName, string path, BaseValue value) : base (productName, path)
        {
            BaseValue = value;
        }
    }
}