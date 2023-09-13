using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using System;

namespace HSMDataCollector.DefaultSensors
{
    internal class ServiceCommandsSensor : SensorBase<string>, IServiceCommandsSensor
    {
        private const string ServiceUpdateCommand = "Service update";


        public ServiceCommandsSensor(InstantSensorOptions options) : base(options) { }


        public void SendCustomCommand(string command, string initiator) =>
            SendValue(command, comment: $"Initiator: {initiator}");


        public void SendRestart(string initiator) => SendCustomCommand("Service restart", initiator);

        public void SendStart(string initiator) => SendCustomCommand("Service start", initiator);

        public void SendStop(string initiator) => SendCustomCommand("Service stop", initiator);


        public void SendUpdate(string initiator) => SendCustomCommand(ServiceUpdateCommand, initiator);

        public void SendUpdate(string initiator, Version newVersion, Version oldVersion = null) =>
            SendUpdate(initiator, newVersion?.ToString(), oldVersion?.ToString());

        public void SendUpdate(string initiator, string newVersion, string oldVersion = null)
        {
            var message = string.IsNullOrEmpty(oldVersion) ? $"to {newVersion}" : $"from {oldVersion} to {newVersion}";

            SendCustomCommand($"{ServiceUpdateCommand} {message}", initiator);
        }
    }
}