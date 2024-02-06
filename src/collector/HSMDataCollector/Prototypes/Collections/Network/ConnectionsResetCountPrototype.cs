using System;
using HSMDataCollector.Alerts;
using HSMDataCollector.Options;
using HSMSensorDataObjects;

namespace HSMDataCollector.Prototypes.Collections.Network
{
    internal sealed class ConnectionsResetCountPrototype : MonitoringInstantSensorOptionsPrototype<MonitoringInstantSensorOptions>
    {
        protected override string SensorName => "Connections Reset Count";
        protected override TimeSpan DefaultPostDataPeriod => TimeSpan.FromMinutes(5);
        protected override string Category => "Network";


        public ConnectionsResetCountPrototype() : base()
        {
            IsComputerSensor = true;
            
            Type = SensorType.DoubleSensor;
            TTL = TimeSpan.FromMinutes(5);
            KeepHistory = TimeSpan.FromDays(90);
            
            TtlAlert = AlertsFactory.IfInactivityPeriodIs().ThenSendNotification($"[$product]$path").AndSetIcon(AlertIcon.Clock).AndSetSensorError().Build();
        }
    }
}