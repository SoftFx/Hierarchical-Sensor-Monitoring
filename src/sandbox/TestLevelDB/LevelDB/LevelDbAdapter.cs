using LevelDB;
using System.Runtime.CompilerServices;
using System.Text;

namespace TestLevelDB.LevelDB
{
    public sealed class LevelDbApabter : IClusterDatabase, IGlobalDatabase, IDisposable
    {
        private readonly static ReadOptions _iteratorOptions = new();

        private readonly static WriteOptions _writeOptions = new() { Sync = true };

        private readonly DB _database;


        public LevelDbApabter(string folder, Options options, int sensorId)
        {
            var dbPath = Path.Combine(Environment.CurrentDirectory, folder, sensorId.ToString());

            var att = 0;

            while (++att < 5)
            {
                try
                {
                    _database = new DB(options, dbPath);
                    break;
                }
                catch
                {
                    if (att == 5)
                        throw;
                }
            }
        }


        public void AddValue(string key, int value)
        {
            //    var keyBytes = GetBytes(key);
            //    var valueBytes = GetBytes();

            _database.Put(key, value.ToString(), _writeOptions);
        }


        string IClusterDatabase.GetFirstValue()
        {
            var iterator = _database.CreateIterator(_iteratorOptions);

            iterator.SeekToFirst();

            return iterator.IsValid() ? GetValue(iterator.Value()) : string.Empty;
        }

        string IClusterDatabase.GetLastValue()
        {
            var iterator = _database.CreateIterator(_iteratorOptions);

            iterator.SeekToLast();

            return GetValue(iterator.Value());
        }

        List<string> IClusterDatabase.GetAllValues()
        {
            var iterator = _database.CreateIterator(_iteratorOptions);

            var values = new List<string>(1 << 10);

            for (iterator.SeekToFirst(); iterator.IsValid(); iterator.Next())
                values.Add(GetValue(iterator.Value()));

            return values;
        }

        string IGlobalDatabase.GetFirstValue(string sensorId)
        {
            var keyBytes = GetBytes(sensorId);
            var iterator = _database.CreateIterator(_iteratorOptions);

            iterator.Seek(keyBytes);

            return iterator.IsValid() ? GetValue(iterator.Value()) : string.Empty;
        }


        string IGlobalDatabase.GetLastValue(string sensorId)
        {
            var prefix = GetBytes($"{sensorId}");
            var keyBytes = GetBytes($"{sensorId}{DateTime.MaxValue.Ticks}");
            var iterator = _database.CreateIterator(_iteratorOptions);

            iterator.Seek(keyBytes);

            if (iterator.IsValid())
                iterator.Prev();
            else
                iterator.SeekToLast();

            var value = iterator.Value();

            return IsStartWith(value, prefix) ? GetValue(value) : string.Empty;
        }

        List<string> IGlobalDatabase.GetAllValues(string sensorId)
        {
            var keyBytes = GetBytes(sensorId);
            var iterator = _database.CreateIterator(_iteratorOptions);

            var values = new List<string>(1 << 10);

            for (iterator.Seek(keyBytes); iterator.IsValid() && IsStartWith(keyBytes, iterator.Key()); iterator.Next())
                values.Add(GetValue(iterator.Value()));

            return values;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte[] GetBytes(string str)
        {
            return Encoding.UTF8.GetBytes(str);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static string GetValue(byte[] bytes)
        {
            return Encoding.UTF8.GetString(bytes);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsStartWith(byte[] initialArray, byte[] anotherBytes)
        {
            int length = Math.Min(initialArray.Length, anotherBytes.Length);

            for (int i = 0; i < length; ++i)
                if (initialArray[i] != anotherBytes[i])
                    return false;

            return true;
        }

        public void Dispose() => _database.Dispose();
    }
}
