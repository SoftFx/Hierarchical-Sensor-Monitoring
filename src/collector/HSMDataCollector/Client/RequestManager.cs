using HSMDataCollector.Converters;
using HSMDataCollector.Logging;
using HSMDataCollector.SensorsMetainfo;
using HSMSensorDataObjects.SensorRequests;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HSMDataCollector.Client
{
    internal class RequestManager : ConcurrentDictionary<string, TaskCompletionSource<bool>>
    {
        private readonly ICollectorLogger _logger;


        private TaskCompletionSource<bool> NewTaskSource => new TaskCompletionSource<bool>();


        internal RequestManager(ICollectorLogger logger) : base()
        {
            _logger = logger;
        }


        internal SensorUpdateRequest RegisterRequest(SensorMetainfo info)
        {
            AddOrUpdate(info.Path, NewTaskSource, (key, val) => NewTaskSource);

            return info.ToApi();
        }

        internal void SetResults(Dictionary<string, string> responses)
        {
            foreach (var response in responses)
            {
                var key = response.Key;
                var error = response.Value;

                if (TryGetValue(key, out var source))
                {
                    var ok = string.IsNullOrEmpty(error);

                    if (!ok)
                        _logger.Error($"Sensor {key} request error: {error}");

                    source?.SetResult(ok);
                }
            }
        }
    }
}
