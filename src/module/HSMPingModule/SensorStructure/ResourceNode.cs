using HSMPingModule.Settings;

namespace HSMPingModule.SensorStructure
{
    internal sealed class ResourceNode : IDisposable
    {
        public List<ResourceSensor> Countries { get; }

        public string HostName { get; }


        internal ResourceNode(KeyValuePair<string, NodeSettings> settings, TimeSpan requestPeriod)
        {
            HostName = settings.Key;
            Countries = settings.Value.Countries.Select(u => new ResourceSensor(HostName, u, settings.Value, requestPeriod)).ToList();
        }


        public void Dispose()
        {
            foreach (var sensor in Countries)
                sensor.Dispose();
        }
    }
}