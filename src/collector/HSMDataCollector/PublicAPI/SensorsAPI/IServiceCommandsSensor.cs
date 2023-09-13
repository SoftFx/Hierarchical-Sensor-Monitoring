using System;

namespace HSMDataCollector.PublicInterface
{
    public interface IServiceCommandsSensor
    {
        void SendCustomCommand(string command, string initiator);

        void SendUpdate(string initiator);

        void SendUpdate(string initiator, string newVersion, string oldVersion = null);

        void SendUpdate(string initiator, Version newVersion, Version oldVersion = default);


        void SendRestart(string initiator);

        void SendStart(string initiator);

        void SendStop(string initiator);
    }
}