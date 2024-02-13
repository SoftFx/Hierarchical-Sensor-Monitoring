using HSMServer.Core.Model;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HSMServer.Dashboards
{
    public record SensorScanResult(long TotalScanned, long TotalMatched, bool IsFinish);


    public sealed class PanelSensorScanTask : TaskCompletionSource
    {
        private const int BatchSize = 50;

        private readonly CancellationTokenSource _tokenSource;
        private long _totalScannedSensors, _totalMatchedSensors;


        public bool IsFinish { get; private set; }


        public async Task StartScanning(IEnumerable<BaseSensorModel> sensors, PanelSubscription subscription)
        {
            var currentScan = 0L;
            var currentMatch = 0L;
            var index = 0L;

            foreach (var sensor in sensors)
            {
                if (_tokenSource.IsCancellationRequested)
                    break;

                if (++index % BatchSize == 0)
                {
                    Interlocked.Add(ref _totalScannedSensors, currentScan);
                    Interlocked.Add(ref _totalMatchedSensors, currentMatch);

                    currentScan = 0;
                    currentMatch = 0;

                    await Task.Yield();
                }

                if (subscription.IsMatch(sensor.FullPath))
                    currentMatch++;

                currentScan++;
            }

            IsFinish = true;
        }

        public SensorScanResult GetResult() => new(Interlocked.Read(ref _totalScannedSensors), Interlocked.Read(ref _totalMatchedSensors), IsFinish);


        public void Cancel()
        {
            IsFinish = true;

            _tokenSource.Cancel();
            SetCanceled(_tokenSource.Token);
        }
    }
}