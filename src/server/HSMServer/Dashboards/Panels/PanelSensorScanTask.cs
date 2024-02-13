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
        private long _totalScannedSensors, _totalAddedSensors;


        public bool IsFinish { get; private set; }


        public async Task StartScanning(IEnumerable<BaseSensorModel> sensors, PanelSubscription subscription)
        {
            var currentScan = 0L;
            var currentAdd = 0L;
            var index = 0L;

            foreach (var sensor in sensors)
            {
                if (_tokenSource.IsCancellationRequested)
                    break;

                if (++index % BatchSize == 0)
                {
                    Interlocked.Add(ref _totalScannedSensors, currentScan);
                    Interlocked.Add(ref _totalAddedSensors, currentAdd);

                    currentScan = 0;
                    currentAdd = 0;

                    await Task.Yield();
                }

                if (subscription.IsMatch(sensor.FullPath))
                    currentAdd++;

                currentScan++;
            }

            IsFinish = true;
        }

        public SensorScanResult GetResult() => new(Interlocked.Read(ref _totalScannedSensors), Interlocked.Read(ref _totalAddedSensors), IsFinish);


        public void Cancel()
        {
            IsFinish = true;

            _tokenSource.Cancel();
            SetCanceled(_tokenSource.Token);
        }
    }
}