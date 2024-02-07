using System;
using HSMDataCollector.Alerts;
using HSMSensorDataObjects;

namespace HSMDataCollector.Prototypes.Collections.Network
{
    internal sealed class ConnectionsResetCountPrototype : NetworkCollectionPrototype
    {
        protected override string SensorName => "Connections Reset Count";


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