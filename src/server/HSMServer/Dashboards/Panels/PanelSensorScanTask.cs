using HSMServer.Core.Model;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HSMServer.Dashboards
{
    public sealed record ScannedSensorInfo(string Path, string Label);


    public sealed record SensorScanResult
    {
        public List<ScannedSensorInfo> MatсhedSensors { get; init; }

        public long TotalScanned { get; init; }

        public long TotalMatched { get; init; }

        public bool IsFinish { get; init; }
    };


    public sealed class PanelSensorScanTask : TaskCompletionSource
    {
        private const int MaxVisibleMathedItems = 20;
        private const int BatchSize = 50;

        private readonly List<ScannedSensorInfo> _matсhedSensors = new(1 << 5);
        private readonly CancellationTokenSource _tokenSource = new();

        private long _totalScanned, _totalMatched;


        public bool IsFinish { get; private set; }


        public async Task StartScanning(IEnumerable<BaseSensorModel> sensors, PanelSubscription subscription)
        {
            foreach (var sensor in sensors)
            {
                if (_tokenSource.IsCancellationRequested)
                    break;

                await Task.Delay(500);

                if (Interlocked.Increment(ref _totalScanned) % BatchSize == 0)
                    await Task.Yield();

                if (subscription.IsMatch(sensor) && Interlocked.Increment(ref _totalMatched) <= MaxVisibleMathedItems)
                    _matсhedSensors.Add(new ScannedSensorInfo(sensor.FullPath, subscription.BuildSensorLabel()));
            }

            IsFinish = true;
        }

        public SensorScanResult GetResult()
        {
            var result = new SensorScanResult
            {
                MatсhedSensors = [.. _matсhedSensors],
                TotalScanned = Interlocked.Read(ref _totalScanned),
                TotalMatched = Interlocked.Read(ref _totalMatched),
                IsFinish = IsFinish,
            };

            _matсhedSensors.Clear();

            return result;
        }


        public void Cancel()
        {
            IsFinish = true;

            _tokenSource.Cancel();
            SetCanceled(_tokenSource.Token);
        }
    }
}