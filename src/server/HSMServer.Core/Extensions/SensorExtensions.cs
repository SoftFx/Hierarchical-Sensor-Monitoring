using System;
using HSMServer.Core.Model;

namespace HSMServer.Core
{
    public static class SensorExtensions
    {
        public static bool IsBar(this SensorType type) => type is SensorType.IntegerBar or SensorType.DoubleBar;


        public static bool IsOk(this SensorStatus status) => status is SensorStatus.Ok;

        public static bool IsOff(this SensorStatus status) => status is SensorStatus.OffTime;

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

        public static BaseValue GetTimeoutBaseValue(this SensorType type)
        {
            BaseValue value = null;

            switch (type)
            {
                case SensorType.Boolean:
                    value = new BooleanValue()
                    {
                        Value = default
                    };
                    break;
                case SensorType.Integer:
                    value = new IntegerValue()
                    {
                        Value = default
                    };
                    break;
                case SensorType.Double:
                    value = new DoubleValue()
                    {
                        Value = default
                    };
                    break;
                case SensorType.String:
                    value = new StringValue()
                    {
                        Value = default
                    };
                    break;
                case SensorType.IntegerBar:
                    value = new IntegerBarValue()
                    {
                        LastValue = default,
                        Min = default,
                        Max = default,
                        Mean = default,
                        Count = default
                    };
                    break;
                case SensorType.DoubleBar:
                    value = new DoubleBarValue()
                    {
                        LastValue = default,
                        Min = default,
                        Max = default,
                        Mean = default,
                        Count = default
                    };;
                    break;
                case SensorType.File:
                    value = new FileValue()
                    {
                        Value = default
                    };
                    break;
                case SensorType.TimeSpan:
                    value = new TimeSpanValue()
                    {
                        Value = default
                    };
                    break;
                case SensorType.Version:
                    value = new VersionValue()
                    {
                        Value = default
                    };
                    break;
            }
            
            return value is null ? null : value with
            {
                Comment = "#Timeout",
                ReceivingTime = DateTime.UtcNow,
                Time = DateTime.UtcNow
            };
        }
    }
}