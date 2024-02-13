using HSMServer.Core.Model;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HSMServer.Dashboards
{
    public sealed class PanelSensorScanTask : TaskCompletionSource
    {
        private const int BatchSize = 50;

        private readonly CancellationTokenSource _tokenSource;
        private long _totalScannedSensors, _totalAddedSensors;


        public bool IsFinished { get; private set; }

        public long TotalScannedSensors => _totalScannedSensors;

        public long TotalAddedSensors => _totalAddedSensors;


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

            IsFinished = true;
        }

        public void Cancel()
        {
            IsFinished = true;

            _tokenSource.Cancel();
            SetCanceled(_tokenSource.Token);
        }
    }
}