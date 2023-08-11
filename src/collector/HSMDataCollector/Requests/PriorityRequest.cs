using HSMSensorDataObjects;
using System;


namespace HSMDataCollector.Requests
{
    public class PriorityRequest
    {
        public Guid Id { get; }

        public (Guid, string) Key { get; }

        public BaseRequest Request { get; }


        public PriorityRequest(BaseRequest request)
        {
            Id = Guid.NewGuid();
            Request = request;

            Key = (Id, request.Path);
        }
    }
}