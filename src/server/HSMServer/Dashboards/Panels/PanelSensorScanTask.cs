using HSMServer.Core.Model;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HSMServer.Dashboards
{
    public record SensorScanResult(List<string> MathedSensors, long TotalScanned, long TotalMatched, bool IsFinish);


    public sealed class PanelSensorScanTask : TaskCompletionSource
    {
        private const int BatchSize = 10;//50;

        private readonly List<string> _mathedSensorsPaths = new(1 << 5);

        private readonly CancellationTokenSource _tokenSource = new();
        private long _totalScannedSensors, _totalMatchedSensors;


        public bool IsFinish { get; private set; }


        public async Task StartScanning(IEnumerable<BaseSensorModel> sensors, PanelSubscription subscription)
        {
            var currentMatch = 0L;
            var currentScan = 0L;

            void FlushResults()
            {
                Interlocked.Add(ref _totalScannedSensors, currentScan);
                Interlocked.Add(ref _totalMatchedSensors, currentMatch);

                currentScan = 0;
                currentMatch = 0;
            }

            foreach (var sensor in sensors)
            {
                if (_tokenSource.IsCancellationRequested)
                    break;
                await Task.Delay(500);
                if (++currentScan % BatchSize == 0)
                {
                    FlushResults();

                    await Task.Yield();
                }

                if (subscription.IsMatch(sensor.FullPath))
                {
                    _mathedSensorsPaths.Add(sensor.FullPath);
                    currentMatch++;
                }
            }

            FlushResults();

            IsFinish = true;
        }

        public SensorScanResult GetResult()
        {
            var result = new SensorScanResult([.. _mathedSensorsPaths], Interlocked.Read(ref _totalScannedSensors), Interlocked.Read(ref _totalMatchedSensors), IsFinish);

            _mathedSensorsPaths.Clear();

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