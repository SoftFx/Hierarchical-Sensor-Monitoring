using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace HSMSensorDataObjects.SensorRequests
{
    public enum Unit : int
    {
        bits = 0,
        bytes = 1,
        KB = 2,
        MB = 3,
        GB = 4,

        Percents = 100,

        Ticks = 1000,
        Milliseconds = 1010,
        Seconds = 1011,
        Minutes = 1012,

        Count = 1100,
        Requests = 1101,
        Responses = 1102,

        Bits_sec = 2100,
        Bytes_sec = 2101,
        KBytes_sec = 2102,
        MBytes_sec = 2103,
        
        ValueInSecond = 3000,
    }

    public enum NoDisplayUnit
    {
        None
    }

    public enum RateDisplayUnit
    {
        PerSecond = 0,
        PerMinute = 1,
        PerHour = 2,
        PerDay = 3,
        PerWeek = 4,
        PerMonth = 5
    }


    [Obsolete("This setting doesn't exist for sensor now")]
    public enum DefaultChatsMode : byte
    {
        FromParent = 0,
        NotInitialized = 1,
        Empty = 2,
    }


    [Flags]
    public enum StatisticsOptions : int
    {
        None = 0,
        EMA = 1,
    }


    [Flags]
    public enum DefaultAlertsOptions : long
    {
        None = 0,
        DisableTtl = 1,
        DisableStatusChange = 2,
    }


    public sealed class AddOrUpdateSensorRequest : CommandRequestBase
    {
        [DefaultValue((int)Command.AddOrUpdateSensor)]
        public override Command Type => Command.AddOrUpdateSensor;


        public List<AlertUpdateRequest> Alerts { get; set; }

        public AlertUpdateRequest TtlAlert { get; set; }


        public SensorType? SensorType { get; set; }

        public string Description { get; set; }


        [Obsolete("This setting doesn't exist for sensor now")]
        public DefaultChatsMode? DefaultChats { get; set; }

        public long? KeepHistory { get; set; }

        public long? SelfDestroy { get; set; }

        public long? TTL { get; set; }


        public StatisticsOptions? Statistics { get; set; }

        public bool? IsSingletonSensor { get; set; }

        public bool? AggregateData { get; set; }


        public bool? EnableGrafana { get; set; }

        public Unit? OriginalUnit { get; set; }

        public int? DisplayUnit { get; set; }

        public DefaultAlertsOptions DefaultAlertsOptions { get; set; }

        public bool IsForceUpdate { get; set; } // if true then DataCollector can chage user settings

        public List<EnumOption> EnumOptions { get; set;}
    }
}