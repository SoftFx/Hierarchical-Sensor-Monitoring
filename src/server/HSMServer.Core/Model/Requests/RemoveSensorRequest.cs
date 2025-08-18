using HSMServer.Core.TableOfChanges;
using System;


namespace HSMServer.Core.Model.Requests
{
    internal class RemoveSensorRequest : BaseRequestModel
    {
        public InitiatorInfo InitiatorInfo { get; init; }

        public Guid? ParentId { get; init; }

        public RemoveSensorRequest(Guid key, string path) : base(key, path)
        {
        }
    }
}
