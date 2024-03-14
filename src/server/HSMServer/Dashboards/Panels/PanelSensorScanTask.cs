﻿using HSMServer.Core.Model;
using NLog;
using System;
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
        public const int MaxVisibleMathedItems = 20;
        private const int BatchSize = 50;

        private static readonly ILogger _logger = LogManager.GetCurrentClassLogger();

        private readonly List<ScannedSensorInfo> _matсhedResult = new(1 << 5);
        private readonly CancellationTokenSource _tokenSource = new();

        private long _totalScanned, _totalMatched;


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

                    if (subscription.IsMatch(sensor))
                    {
                        MatchedSensors.Add(sensor);

                        if (Interlocked.Increment(ref _totalMatched) <= MaxVisibleMathedItems)
                            _matсhedResult.Add(new ScannedSensorInfo(sensor.FullPath, subscription.BuildSensorLabel()));
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

        private void AddMathedSensor(BaseSensorModel sensor, PanelSubscription subscription)
        {
            var info = new ScannedSensorInfo(sensor.FullPath, subscription.BuildSensorLabel());

            _matсhedResult.Add(info);
            MatchedSensors.Add(sensor);
        }
    }
}