using HSMSensorDataObjects;
using System;


namespace HSMDataCollector.Requests
{
    public class PriorityRequest
    {
        public Guid Id { get; }

        public BaseRequest Request { get; }


        public PriorityRequest(BaseRequest request)
        {
            Id = Guid.NewGuid();
            Request = request;
        }
    }
}