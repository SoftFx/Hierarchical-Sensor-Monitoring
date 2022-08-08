using TestLevelDB.LevelDB;

namespace TestLevelDB.Server
{
    public class SingleServer : ServerBase, IServer
    {
        private IGlobalDatabase? _database = null;


        public SingleServer() : base()
        {
            _options.WriteBufferSize = Program.DbBufferSize;
        }


        public virtual Task InitServer()
        {
            AddDatabase();

            return Task.CompletedTask;
        }

        public Task CloseDatabase()
        {
            _database?.Dispose();

            return Task.CompletedTask;
        }


        public Task ReadEachFirstSensorData()
        {
            for (int sensorId = 0; sensorId < Program.SensorsCnt; ++sensorId)
                _database?.GetFirstValue($"{sensorId}_");

            return Task.CompletedTask;
        }

        public Task ReadEachLastSensorData()
        {
            for (int sensorId = 0; sensorId < Program.SensorsCnt; ++sensorId)
                _database?.GetLastValue($"{sensorId}_");

            return Task.CompletedTask;
        }

        public Task ReadEachAllSensorData()
        {
            for (int sensorId = 0; sensorId < Program.SensorsCnt; ++sensorId)
                _database?.GetAllValues($"{sensorId}_");

            return Task.CompletedTask;
        }


        protected override void AddDataToSensor(Data item)
        {
            var key = item.Time;
            var value = item.Value;
            var sensor = item.Value / Program.SensorsCntData;

            _database?.AddValue($"{sensor}_{key}", value);
        }

        protected override void AddDatabase(int sensorId = 0)
        {
            _database ??= new LevelDbApabter(Type, _options, sensorId);
        }
    }
}