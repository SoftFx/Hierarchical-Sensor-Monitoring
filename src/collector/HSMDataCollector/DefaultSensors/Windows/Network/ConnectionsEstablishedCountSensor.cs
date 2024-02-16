﻿using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Windows.Network
{
    internal sealed class ConnectionsEstablishedCountSensor : BaseSocketsSensor
    {
        protected override string CounterName => "Connections Established";


        internal ConnectionsEstablishedCountSensor(MonitoringInstantSensorOptions options) : base(options) { }
    }
}