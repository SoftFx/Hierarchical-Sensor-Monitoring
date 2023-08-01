namespace HSMDataCollector.PublicInterface
{
    public interface IServiceCommandsSensor
    {
        void SendCommand(string command, string initiator);


        void SendRestart(string initiator);

        void SendUpdate(string initiator);
    }
}