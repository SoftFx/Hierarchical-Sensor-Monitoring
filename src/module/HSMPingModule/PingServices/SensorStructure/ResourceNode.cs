using HSMPingModule.Settings;

namespace HSMPingModule.PingServices
{
    internal sealed class ResourceNode : IDisposable
    {
        public List<ResourceSensor> Countries { get; }

        public string HostName { get; }


        internal ResourceNode(KeyValuePair<string, NodeSettings> settings)
        {
            HostName = settings.Key;
            Countries = settings.Value.Countries.Select(u => new ResourceSensor(HostName, u, settings.Value)).ToList();
        }


        public void Dispose()
        {
            foreach (var sensor in Countries)
                sensor.Dispose();
        }
    }
}