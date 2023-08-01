using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using System;

namespace HSMDataCollector.DefaultSensors
{
    internal class ServiceCommandsSensor : SensorBase<string>, IServiceCommandsSensor
    {
        protected override string SensorName => "Service commands";


        public ServiceCommandsSensor(SensorOptions options) : base(options) { }


        public void SendCustomCommand(string command, string initiator) =>
            SendValue(command, comment: $"Initiator: {initiator}, Time: {DateTime.UtcNow.ToString(DefaultTimeFormat)}");


        public void SendRestart(string initiator) => SendCustomCommand("Service restart", initiator);

        public void SendUpdate(string initiator) => SendCustomCommand("Service update", initiator);

        public void SendStart(string initiator) => SendCustomCommand("Service start", initiator);

        public void SendStop(string initiator) => SendCustomCommand("Service stop", initiator);
    }
}