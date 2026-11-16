using System;
using System.IO;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMDatabase.LevelDB;
using HSMDatabase.LevelDB.DatabaseImplementations;
using Xunit;

namespace HSMDatabase.LevelDB.Tests.SensorsDBTests
{
    // The worker writes a sensor row in two ways (#1756). AddSensor and AddSensorIdToList do
    // what they always did with a write LevelDB fails on: log it and return, so the caller is
    // not told. WriteSensor and WriteSensorIdToList are the same writes with their outcome:
    // null when written, the failure otherwise, still not thrown. The registration of a
    // sensor asks for the outcome; every other caller keeps the void methods.
    //
    // A real LevelDB in a temp directory, behind an adapter whose Put fails on demand: LevelDB
    // itself gives a test no way to make a write fail.
    public sealed class SensorRowWriteOutcomeTests : IDisposable
    {
        private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"hsm-sensor-row-tests-{Guid.NewGuid():N}");
        private readonly FailingPutAdapter _adapter;
        private readonly EnvironmentDatabaseWorker _worker;


        public SensorRowWriteOutcomeTests()
        {
            Directory.CreateDirectory(_dbPath);

            _adapter = new FailingPutAdapter(_dbPath);
            _worker = new EnvironmentDatabaseWorker(_adapter);
        }


        public void Dispose()
        {
            _worker.Dispose();

            try
            {
                Directory.Delete(_dbPath, true);
            }
            catch (IOException)
            {
                // LevelDB releases file handles asynchronously on Windows; a leftover temp
                // directory is harmless for the test outcome.
            }
        }


        [Fact]
        public void WriteSensor_ReportsNothing_WhenTheRowIsWritten()
        {
            var sensor = Sensor();

            Assert.Null(_worker.WriteSensorIdToList(sensor.Id));
            Assert.Null(_worker.WriteSensor(sensor));

            Assert.Contains(sensor.Id, _worker.GetAllSensorsIds());
            Assert.Equal(sensor.DisplayName, _worker.GetSensorEntity(sensor.Id).DisplayName);
        }

        [Fact]
        public void WriteSensor_ReportsTheFailure_AndDoesNotThrow_WhenLevelDbFailsOnTheWrite()
        {
            var sensor = Sensor();

            _adapter.FailWrites = true;

            var failure = _worker.WriteSensor(sensor);

            Assert.NotNull(failure);
            Assert.Equal(FailingPutAdapter.Failure, failure.Message);

            _adapter.FailWrites = false;

            Assert.Null(_worker.GetSensorEntity(sensor.Id));

            // The write that fails once passes on the repeat.
            Assert.Null(_worker.WriteSensor(sensor));
            Assert.NotNull(_worker.GetSensorEntity(sensor.Id));
        }

        [Fact]
        public void WriteSensorIdToList_ReportsTheFailure_AndDoesNotThrow_WhenLevelDbFailsOnTheWrite()
        {
            var sensor = Sensor();

            _adapter.FailWrites = true;

            var failure = _worker.WriteSensorIdToList(sensor.Id);

            Assert.NotNull(failure);

            _adapter.FailWrites = false;

            Assert.DoesNotContain(sensor.Id, _worker.GetAllSensorsIds());
        }

        // What every caller but the registration relies on: the void methods neither throw
        // nor report, whatever LevelDB did with the write.
        [Fact]
        public void TheVoidMethods_StillSwallowAFailedWrite()
        {
            var sensor = Sensor();

            _adapter.FailWrites = true;

            _worker.AddSensorIdToList(sensor.Id);
            _worker.AddSensor(sensor);

            _adapter.FailWrites = false;

            Assert.DoesNotContain(sensor.Id, _worker.GetAllSensorsIds());
            Assert.Null(_worker.GetSensorEntity(sensor.Id));

            // And they write as before when LevelDB does.
            _worker.AddSensorIdToList(sensor.Id);
            _worker.AddSensor(sensor);

            Assert.Contains(sensor.Id, _worker.GetAllSensorsIds());
            Assert.NotNull(_worker.GetSensorEntity(sensor.Id));
        }


        private static SensorEntity Sensor() => new()
        {
            Id = Guid.NewGuid().ToString(),
            ProductId = Guid.NewGuid().ToString(),
            DisplayName = "row",
            CreationDate = DateTime.UtcNow.Ticks,
        };


        private sealed class FailingPutAdapter(string name) : LevelDBDatabaseAdapter(name)
        {
            public const string Failure = "IO error: the disk is full";

            public bool FailWrites { get; set; }


            public override void Put(byte[] key, byte[] value)
            {
                // As the adapter itself answers a write LevelDB failed on.
                if (FailWrites)
                    throw new ServerDatabaseException(Failure, new IOException(Failure));

                base.Put(key, value);
            }
        }
    }
}
