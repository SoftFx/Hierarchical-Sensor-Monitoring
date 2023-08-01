namespace HSMDataCollector.PublicInterface
{
    public interface IServiceCommandsSensor
    {
        void SendCustomCommand(string command, string initiator);


        void SendRestart(string initiator);

        void SendUpdate(string initiator);

        void SendStart(string initiator);

        void SendStop(string initiator);
    }
}