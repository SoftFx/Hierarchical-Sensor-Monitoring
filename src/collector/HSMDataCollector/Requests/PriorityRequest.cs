using HSMSensorDataObjects;
using System;


namespace HSMDataCollector.Requests
{
    public readonly struct PriorityRequest
    {
        public (Guid, string) Key { get; }

        public CommandRequestBase Request { get; }


        public PriorityRequest(CommandRequestBase request)
        {
            Key = (Guid.NewGuid(), request.Path);
            Request = request;
        }
    }
}