using System.Collections.Concurrent;
using TestLevelDB.LevelDB;

namespace TestLevelDB.Server
{
    public class ClusterServer : ServerBase, IServer
    {
        private readonly ConcurrentDictionary<int, IClusterDatabase> _clusters = new();


        public ClusterServer() : base()
        {
            _options.WriteBufferSize = Program.DbBufferSize / Program.SensorsCnt;
        }


        public virtual Task InitServer()
        {
            for (int i = 0; i < Program.SensorsCnt; i++)
                AddDatabase(i);

            return Task.CompletedTask;
        }

        public Task CloseDatabase()
        {
            foreach (var (_, db) in _clusters)
                db.Dispose();

            return Task.CompletedTask;
        }


        public Task ReadEachFirstSensorData()
        {
            for (int sensorId = 0; sensorId < Program.SensorsCnt; ++sensorId)
                _clusters[sensorId].GetFirstValue();

            return Task.CompletedTask;
        }

        public Task ReadEachLastSensorData()
        {
            for (int sensorId = 0; sensorId < Program.SensorsCnt; ++sensorId)
                _clusters[sensorId].GetLastValue();

            return Task.CompletedTask;
        }

        public Task ReadEachAllSensorData()
        {
            for (int sensorId = 0; sensorId < Program.SensorsCnt; ++sensorId)
                _clusters[sensorId].GetAllValues();

            return Task.CompletedTask;
        }


        protected override void AddDataToSensor(Data item)
        {
            var key = item.Time;
            var value = item.Value;
            var sensor = item.Value / Program.SensorsCntData;

            _clusters[sensor].AddValue($"{key}", value);
        }

        protected override void AddDatabase(int sensorId)
        {
            var db = new LevelDbApabter(Type, _options, sensorId);

            _clusters.TryAdd(sensorId, db);
        }
    }
}