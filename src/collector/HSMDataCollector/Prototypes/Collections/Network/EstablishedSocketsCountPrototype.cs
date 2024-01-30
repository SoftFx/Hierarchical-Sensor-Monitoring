using System;
using HSMDataCollector.Alerts;
using HSMDataCollector.Options;
using HSMSensorDataObjects;

namespace HSMDataCollector.Prototypes.Collections.Network
{
    internal sealed class EstablishedSocketsCountPrototype : MonitoringInstantSensorOptionsPrototype<SocketSensorOptions>
    {
        protected override string SensorName => "Established sockets count";
        protected override TimeSpan DefaultPostDataPeriod => TimeSpan.FromMinutes(5);
        protected override string Category => "Network";


        public EstablishedSocketsCountPrototype() : base()
        {
            IsComputerSensor = true;
            
            Type = SensorType.IntSensor;
            TTL = TimeSpan.FromMinutes(5);
            KeepHistory = TimeSpan.FromDays(90);
            
            TtlAlert = AlertsFactory.IfInactivityPeriodIs().ThenSendNotification($"[$product]$path").AndSetIcon(AlertIcon.Clock).AndSetSensorError().Build();
        }
        
        public override SocketSensorOptions Get(SocketSensorOptions customOptions)
        {
            var options = base.Get(customOptions);

            return options;
        }
    }
}