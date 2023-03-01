﻿using HSMServer.Core.Model;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HSMServer.Extensions;

namespace HSMServer.Notifications
{
    internal sealed class CHash
    {
        private const int MaxSensorsCount = 10;

        private readonly HashSet<string> _hash = new();
        private readonly object _lock = new();


        internal bool IsEmpty => _hash.Count == 0;


        internal void Push(string sensor)
        {
            lock (_lock)
            {
                _hash.Add(sensor);
            }
        }

        internal void RemoveSensor(string sensor)
        {
            lock (_lock)
            {
                _hash.Remove(sensor);
            }
        }

        internal void Clear()
        {
            lock (_lock)
            {
                _hash.Clear();
            }
        }

        internal string GenerateOutputSensors(int nodeSensorsCount)
        {
            lock (_lock)
            {
                var sensors = string.Join(", ", _hash.TakeLast(MaxSensorsCount));

                if (_hash.Count > MaxSensorsCount)
                    sensors = $"{sensors} ... ({_hash.Count}/{nodeSensorsCount})";

                Clear();

                return sensors;
            }
        }
    }


    internal abstract class CDictBase<T, U> : ConcurrentDictionary<T, U> where U : class, new()
    {
        public new U this[T key] => GetOrAdd(key);


        internal U GetOrAdd(T key)
        {
            if (!TryGetValue(key, out U value))
            {
                base[key] = new U();

                return base[key];
            }

            return value;
        }
    }


    internal sealed class CDict<T> : CDictBase<string, T> where T : class, new() { }


    internal sealed class CTupleDict<T> : CDictBase<(string, string), T> where T : class, new() { }


    internal sealed class MessageBuilder
    {
        private readonly CDict<CDict<CTupleDict<CHash>>> _messageTree = new();
        private readonly ConcurrentDictionary<string, int> _nodeSensorsCount = new();
        private readonly ConcurrentDictionary<Guid, (string statusPath, string message)> _oldStatusPaths = new();



        internal DateTime ExpectedSendingTime { get; private set; } = DateTime.UtcNow;


        internal void AddMessage(BaseSensorModel sensor)
        {
            var product = sensor.RootProductName;
            var nodePath = sensor?.ParentProduct?.Path ?? string.Empty;
            var message = sensor.ValidationResult.Message;
            var statusIcon = sensor.ValidationResult.Result.ToStatusIcon();

            var pathDict = _messageTree[product][nodePath];

            if (_oldStatusPaths.TryGetValue(sensor.Id, out var oldStatusPath))
            {
                pathDict[oldStatusPath].RemoveSensor(sensor.DisplayName);
                statusIcon = $"{oldStatusPath.statusPath}->{statusIcon}";
            }

            _nodeSensorsCount[nodePath] = sensor.ParentProduct?.Sensors?.Count ?? 0;
            _oldStatusPaths[sensor.Id] = (statusIcon, message);

            pathDict[(statusIcon, message)].Push(sensor.DisplayName);
        }

        internal string GetAggregateMessage(int notificationMessageDelay)
        {
            var builder = new StringBuilder(1 << 8);

            foreach (var (productName, nodes) in _messageTree)
            {
                foreach (var (nodePath, changePath) in nodes)
                {
                    foreach (var ((statusPath, message), sensors) in changePath)
                    {
                        if (!sensors.IsEmpty)
                            BuildMessage(builder, productName, statusPath, message, sensors.GenerateOutputSensors(_nodeSensorsCount[nodePath]), nodePath);
                    }

                    changePath.Clear();
                }

                nodes.Clear();

                builder.AppendLine();
            }

            ExpectedSendingTime = GetNextNotificationTime(notificationMessageDelay);

            _oldStatusPaths.Clear();
            _nodeSensorsCount.Clear();

            return builder.ToString();
        }

        public static string GetSingleMessage(BaseSensorModel sensor)
        {
            var builder = new StringBuilder(1 << 2);

            var product = sensor.RootProductName;
            var nodePath = sensor?.ParentProduct?.Path ?? string.Empty;
            var message = sensor.ValidationResult.Message;
            var result = sensor.ValidationResult.Result.ToStatusIcon();

            BuildMessage(builder, product, result, message, sensor.DisplayName, nodePath);

            return builder.ToString();
        }

        private static void BuildMessage(StringBuilder builder, string productName, string statusPath, string message, string sensors, string nodePath)
        {
            productName = $"[{productName}]".EscapeMarkdownV2();
            statusPath = statusPath.EscapeMarkdownV2();
            sensors = $"/[{sensors}]".EscapeMarkdownV2();

            if (!string.IsNullOrEmpty(nodePath))
                nodePath = $"{nodePath}".EscapeMarkdownV2();

            if (!string.IsNullOrEmpty(message))
                message = $" = {message}".EscapeMarkdownV2();

            builder.AppendLine($"{statusPath} {productName}{nodePath}{sensors}{message}");
        }

        private static DateTime GetNextNotificationTime(int notificationsDelay)
        {
            var ticks = DateTime.MinValue.AddSeconds(notificationsDelay).Ticks;
            var start = DateTime.UtcNow.Ticks / ticks * ticks;

            return new DateTime(start + ticks);
        }
    }
}
