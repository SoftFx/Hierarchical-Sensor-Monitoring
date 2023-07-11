using HSMDatabase.AccessManager;
using HSMDatabase.AccessManager.DatabaseEntities.SnapshotEntity;
using HSMServer.Core.DataLayer;
using System.Threading.Tasks;

namespace HSMServer.Core.TreeStateSnapshot
{
    public sealed class TreeStateSnapshot : ITreeStateSnapshot
    {
        private readonly StateCollection<LastSensorState, SensorStateEntity> _sensors = new();
        private readonly ISnapshotDatabase _db;


        public ISnapshotCollection<LastSensorState> Sensors => _sensors;


        public bool HasData { get; } = true;

        public bool IsFinal { get; }


        public TreeStateSnapshot(IDatabaseCore db)
        {
            _db = db.Snapshots;

            if (_db.TryGetLastNode(out var node))
            {
                _sensors = new StateCollection<LastSensorState, SensorStateEntity>(node.Sensors);

                IsFinal = node.IsFinal;
            }
            else
                HasData = false;
        }


        public Task FlushState(bool isFinal)
        {
            var node = _db.BuildNode(isFinal);

            node.Sensors.Data = _sensors.GetStates();

            return node.Save();
        }
    }
}
