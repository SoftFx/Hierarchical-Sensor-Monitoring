using HSMServer.Core.DataLayer;

namespace HSMServer.Core.TreeStateSnapshot
{
    public sealed class TreeStateSnapshot : ITreeStateSnapshot
    {
        private readonly IDatabaseCore _db;


        public StateCollection<LastSensorState> Sensors { get; } = new();


        public TreeStateSnapshot(IDatabaseCore db)
        {
            _db = db;
        }

        public void FlushState()
        {
            _db.AddSnapshot();
        }
    }
}
