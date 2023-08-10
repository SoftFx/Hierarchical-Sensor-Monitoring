using System;
using System.Numerics;
using HSMServer.Core.Model;

namespace HSMServer.Core
{
    public static class SensorExtensions
    {
        public static bool IsBar(this SensorType type) => type is SensorType.IntegerBar or SensorType.DoubleBar;


        public static bool IsOk(this SensorStatus status) => status is SensorStatus.Ok;

        public static bool IsOfftime(this SensorStatus status) => status is SensorStatus.OffTime;

        public static bool IsError(this SensorStatus status) => status is SensorStatus.Error;


        public static string ToIcon(this SensorStatus status) => status switch
        {
            SensorStatus.Ok => "✅",
            SensorStatus.Error => "❌",
            SensorStatus.OffTime => "💤",
            _ => "❓"
        };

        public static bool HasGrafana(this Integration integration) => integration.HasFlag(Integration.Grafana);

        public static SensorStatus ToStatus(this byte status) => status switch
        {
            (byte)SensorStatus.Ok => SensorStatus.Ok,
            (byte)SensorStatus.OffTime => SensorStatus.OffTime,
            _ => SensorStatus.Error,
        };

        public static BaseValue GetTimeoutBaseValue(this SensorType type, DateTime lastUpdateTime, string value)
        {
            return type switch
            {
                SensorType.Boolean => BuildDefault<BooleanValue>(),
                SensorType.Integer => BuildDefault<IntegerValue>(),
                SensorType.Double => BuildDefault<DoubleValue>(),
                SensorType.String => BuildDefault<StringValue>(),
                SensorType.IntegerBar => BuildDefault<IntegerBarValue>(),
                SensorType.DoubleBar => BuildDefault<DoubleBarValue>(),
                SensorType.File => BuildDefault<FileValue>(),
                SensorType.TimeSpan => BuildDefault<TimeSpanValue>(),
                SensorType.Version => BuildDefault<VersionValue>(),
                _ => throw new ArgumentException($"Sensor type = {type} is not valid")
            };
            
            T BuildDefault<T>() where T : BaseValue, new()
            {
                return new T()
                {
                    ReceivingTime = DateTime.UtcNow,
                    Time = DateTime.UtcNow,
                    Comment = $"{BaseSensorModel.TimeoutComment} - {lastUpdateTime}, {value}"
                };
            }
        }
    }
}