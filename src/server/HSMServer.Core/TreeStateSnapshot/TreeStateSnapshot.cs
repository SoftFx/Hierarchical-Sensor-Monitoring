using HSMDatabase.AccessManager;
using HSMDatabase.AccessManager.DatabaseEntities.SnapshotEntity;
using HSMServer.Core.DataLayer;
using System.Threading.Tasks;
using HSMServer.Core.TreeStateSnapshot.States;

namespace HSMServer.Core.TreeStateSnapshot
{
    public sealed class TreeStateSnapshot : ITreeStateSnapshot
    {
        private readonly StateCollection<LastSensorState, SensorStateEntity> _sensors = new();
        private readonly StateCollection<LastKeyState, LastKeyStateEntity> _keys = new();
        private readonly ISnapshotDatabase _db;


        public ISnapshotCollection<LastSensorState> Sensors => _sensors;

        public ISnapshotCollection<LastKeyState> Keys => _keys;

        public bool HasData { get; } = true;

        public bool IsFinal { get; }


        public TreeStateSnapshot(IDatabaseCore db)
        {
            _db = db.Snapshots;

            if (_db.TryGetLastNode(out var node))
            {
                try
                {
                    _sensors = new StateCollection<LastSensorState, SensorStateEntity>(node.Sensors);
                    _keys = new StateCollection<LastKeyState, LastKeyStateEntity>(node.Keys);

                    IsFinal = node.IsFinal;
                }
                catch 
                {
                    HasData = false;
                }
            }
            else
                HasData = false;
        }


        public Task FlushState(bool isFinal)
        {
            var node = _db.BuildNode(isFinal);

            node.Sensors.Data = _sensors.GetStates();
            node.Keys.Data = _keys.GetStates();

            return node.Save();
        }
    }
}
