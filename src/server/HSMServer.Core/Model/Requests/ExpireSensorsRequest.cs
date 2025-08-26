using HSMServer.Core.SensorsUpdatesQueue;


namespace HSMServer.Core.Model.Requests
{
    internal record ExpireSensorsRequest(BaseSensorModel[] Sensors) : IUpdateRequest
    {
        public BaseSensorModel[] Sensors { get; } = Sensors;

    }
}
