using HSMServer.Core.Model;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HSMServer.Dashboards
{
    public record SensorScanResult(List<string> MatсhedSensors, long TotalScanned, long TotalMatched, bool IsFinish);


    public sealed class PanelSensorScanTask : TaskCompletionSource
    {
        private const int BatchSize = 10;//50;

        private readonly List<string> _matсhedSensorsPaths = new(1 << 5);

        private readonly CancellationTokenSource _tokenSource = new();
        private long _totalScannedSensors, _totalMatchedSensors;


        public bool IsFinish { get; private set; }


        public async Task StartScanning(IEnumerable<BaseSensorModel> sensors, PanelSubscription subscription)
        {
            foreach (var sensor in sensors)
            {
                if (_tokenSource.IsCancellationRequested)
                    break;

                await Task.Delay(500);

                if (Interlocked.Increment(ref _totalScannedSensors) % BatchSize == 0)
                    await Task.Yield();

                if (subscription.IsMatch(sensor.FullPath))
                {
                    Interlocked.Increment(ref _totalMatchedSensors);
                    _matсhedSensorsPaths.Add(sensor.FullPath);
                }
            }

            IsFinish = true;
        }

        public SensorScanResult GetResult()
        {
            var result = new SensorScanResult([.. _matсhedSensorsPaths], Interlocked.Read(ref _totalScannedSensors), Interlocked.Read(ref _totalMatchedSensors), IsFinish);

            _matсhedSensorsPaths.Clear();

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