namespace HSMServer.Core.TreeStateSnapshot
{
    public interface ITreeStateSnapshot
    {
        public StateCollection<LastSensorState> Sensors { get; }


        public void FlushState();
    }
}
