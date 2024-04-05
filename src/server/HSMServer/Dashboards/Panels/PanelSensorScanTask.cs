using HSMServer.Core.Model;
using NLog;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HSMServer.Dashboards
{
    public sealed record ScannedSensorInfo
    {
        public string Path { get; init; }

        public string Type { get; init; }

        public string Label { get; init; }

        public bool IsPropertyIncorrect { get; init; }

        public bool IsEmaPropertyIncorrect { get; init; }
    }


    public sealed record SensorScanResult
    {
        public List<ScannedSensorInfo> MatсhedSensors { get; init; }

        public long IncorrectEmaProperty { get; init; }

        public long IncorrectProperty { get; init; }

        public long TotalScanned { get; init; }

        public long TotalMatched { get; init; }

        public bool IsFinish { get; init; }
    };


    public sealed class PanelSensorScanTask : TaskCompletionSource
    {
        public const int MaxVisibleMatchedItems = 20;
        private const int BatchSize = 50;

        private static readonly ILogger _logger = LogManager.GetCurrentClassLogger();

        private readonly List<ScannedSensorInfo> _matсhedResult = new(1 << 5);
        private readonly CancellationTokenSource _tokenSource = new();

        private long _totalScanned, _totalMatched, _incorrectProperty, _incorrectEmaProperty;


        public List<BaseSensorModel> MatchedSensors { get; } = new(1 << 4);

        public bool IsFinish { get; private set; }


        public async Task StartScanning(IEnumerable<BaseSensorModel> sensors, PanelSubscription subscription)
        {
            foreach (var sensor in sensors)
            {
                try
                {
                    if (_tokenSource.IsCancellationRequested)
                        break;

                    if (Interlocked.Increment(ref _totalScanned) % BatchSize == 0)
                        await Task.Yield();

                    if (subscription.IsMatchTemplate(sensor))
                    {
                        bool isPropertyIncorrect = false;
                        bool isEmaIncorrect = false;

                        if (!subscription.IsPropertySuitable(sensor))
                        {
                            Interlocked.Increment(ref _incorrectProperty);
                            isPropertyIncorrect = true;
                        }
                        else if (!subscription.IsEmaPropertySuitable(sensor))
                        {
                            Interlocked.Increment(ref _incorrectEmaProperty);
                            isEmaIncorrect = true;
                        }

                        MatchedSensors.Add(sensor);

                        if (Interlocked.Increment(ref _totalMatched) <= MaxVisibleMatchedItems)
                            _matсhedResult.Add(new ScannedSensorInfo()
                            {
                                Path = sensor.FullPath,
                                Type = sensor.Type.ToString(),
                                Label = subscription.BuildSensorLabel(),
                                IsPropertyIncorrect = isPropertyIncorrect,
                                IsEmaPropertyIncorrect = isEmaIncorrect
                            });
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error($"Error scanning sensor {sensor.Id} for template - {ex.Message}");
                }
            }

            IsFinish = true;
        }

        public SensorScanResult GetResult()
        {
            var result = new SensorScanResult
            {
                MatсhedSensors = [.. _matсhedResult],
                TotalScanned = Interlocked.Read(ref _totalScanned),
                TotalMatched = Interlocked.Read(ref _totalMatched),
                IncorrectProperty = Interlocked.Read(ref _incorrectProperty),
                IncorrectEmaProperty = Interlocked.Read(ref _incorrectEmaProperty),
                IsFinish = IsFinish,
            };

            _matсhedResult.Clear();

            return result;
        }


        public void Cancel()
        {
            IsFinish = true;

            _tokenSource.Cancel();
            TrySetCanceled(_tokenSource.Token);
        }
    }
}