using HSMServer.Core.DataLayer;
using System.Linq;

namespace HSMServer.Core.TreeStateSnapshot
{
    public sealed class TreeStateSnapshot : ITreeStateSnapshot
    {
        private readonly IDatabaseCore _db;


        public StateCollection<LastSensorState> Sensors { get; }


        public TreeStateSnapshot(IDatabaseCore db)
        {
            _db = db;

            Sensors = new StateCollection<LastSensorState>(_db.GetSensorSnapshot().ToDictionary(k => k.Key, v => new LastSensorState(v.Value)));
        }


        public void FlushState()
        {
            _db.SaveSensorSnapshot(Sensors.Where(u => !u.Value.IsDefault).ToDictionary(k => k.Key, v => v.Value.ToEntity()));
        }
    }
}
