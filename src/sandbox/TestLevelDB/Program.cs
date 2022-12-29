using LevelDB;
using System.Text;
using TestLevelDB.Server;

namespace TestLevelDB
{
    internal static class Program
    {
        public const int SensorsCnt = 10_000;
        public const int SensorsCntData = 2000;
        
        public const int ThreadsCount = 10;
        public const long DbBufferSize = 1024 * 1024 * 1024;

        public const int TotalDataCnt = SensorsCntData * SensorsCnt;


        private static async Task Main(string[] _)
        {
            //var servers = new IServer[]
            //{
            //    new SingleServer(),
            //    new ClusterServerThreadPool(),
            //};

            //foreach (var server in servers)
            //    await RunningContext.RunTests(server);

            //RunningContext.PrintStats();

            //Console.ReadLine();
            //Console.WriteLine("Finish");

            //foreach (var s in servers)
            //    await s.CloseDatabase();

            var dbPath = Path.Combine(Environment.CurrentDirectory, "test_db");

            if (Directory.Exists(dbPath))
                Directory.Delete(dbPath, true);

            Directory.CreateDirectory(dbPath);

            var options = new Options()
            {
                CreateIfMissing = true,
                MaxOpenFiles = 100000,
                CompressionLevel = CompressionLevel.SnappyCompression,
                WriteBufferSize = 8 * 1024 * 1024,
            };


            var database = new DB(dbPath, options);

            void DbAdd(string key, int val)
            {
                var keyBytes = Encoding.UTF8.GetBytes(key);
                var valBytes = Encoding.UTF8.GetBytes(val.ToString());

                database.Put(keyBytes, valBytes);
            }

            DbAdd("1_230", 230);
            DbAdd("1_240", 240);
            DbAdd("1_300", 300);
            DbAdd("2_010", 201);
            DbAdd("2_100", 210);

            ThreadPool.QueueUserWorkItem(_ => AddValues(database));

            await foreach (var value in GetValueFromTo(database, Encoding.UTF8.GetBytes("1_000"), Encoding.UTF8.GetBytes("1_999")))
            {
                Console.WriteLine(Encoding.UTF8.GetString(value));
            }

            //var iterator = database.CreateIterator();

            //iterator.Seek("2_11");
            //iterator.Prev();

            //var value = iterator.Value();



            //Console.WriteLine(Encoding.UTF8.GetString(value));

            Console.ReadLine();

            database.Dispose();
        }

        private static void AddValues(DB database)
        {
            var rand = new Random(123);

            for (int i = 0; i < 700; ++i)
            {
                var key = rand.Next(100, 1000);
                database.Put($"1_{key}", $"{key}");

                Console.WriteLine("+");
            }
        }

        public static async IAsyncEnumerable<byte[]> GetValueFromTo(DB _database, byte[] from, byte[] to)
        {
            Iterator iterator = null;

            try
            {
                iterator = _database.CreateIterator();

                for (iterator.Seek(from); iterator.IsValid && iterator.Key().IsSmallerOrEquals(to); iterator.Next())
                    yield return iterator.Value();
            }
            finally
            {
                iterator?.Dispose();
            }
        }

        public static async IAsyncEnumerable<byte[]> GetValueToFrom(DB _database, byte[] from, byte[] to)
        {
            Iterator iterator = null;

            try
            {
                iterator = _database.CreateIterator();

                iterator.Seek(to);

                if (!iterator.IsValid)
                    iterator.SeekToLast();

                while (iterator.IsValid && iterator.Key().IsGreater(to))
                {
                    iterator.Prev();

                    if (!iterator.IsValid || iterator.Key().IsSmaller(from))
                        yield break;
                }

                for (; iterator.IsValid && iterator.Key().IsGreaterOrEquals(from); iterator.Prev())
                    yield return iterator.Value();
            }
            finally
            {
                iterator?.Dispose();
            }
        }

        internal static bool IsGreaterOrEquals(this byte[] initialBytes, byte[] anotherBytes)
        {
            if (initialBytes.Length != anotherBytes.Length)
                return initialBytes.Length > anotherBytes.Length;

            for (int i = 0; i < initialBytes.Length; ++i)
            {
                var cmpResult = initialBytes[i].CompareTo(anotherBytes[i]);

                if (cmpResult != 0)
                    return cmpResult > 0;
            }

            return true;
        }

        internal static bool IsGreater(this byte[] initialBytes, byte[] anotherBytes)
        {
            if (initialBytes.Length != anotherBytes.Length)
                return initialBytes.Length > anotherBytes.Length;

            for (int i = 0; i < initialBytes.Length; ++i)
            {
                var cmpResult = initialBytes[i].CompareTo(anotherBytes[i]);

                if (cmpResult != 0)
                    return cmpResult > 0;
            }

            return false;
        }

        internal static bool IsSmallerOrEquals(this byte[] initialBytes, byte[] anotherBytes)
        {
            if (initialBytes.Length != anotherBytes.Length)
                return initialBytes.Length < anotherBytes.Length;

            for (int i = 0; i < initialBytes.Length; ++i)
            {
                var cmpResult = initialBytes[i].CompareTo(anotherBytes[i]);

                if (cmpResult != 0)
                    return cmpResult < 0;
            }

            return true;
        }

        internal static bool IsSmaller(this byte[] initialBytes, byte[] anotherBytes)
        {
            if (initialBytes.Length != anotherBytes.Length)
                return initialBytes.Length < anotherBytes.Length;

            for (int i = 0; i < initialBytes.Length; ++i)
            {
                var cmpResult = initialBytes[i].CompareTo(anotherBytes[i]);

                if (cmpResult != 0)
                    return cmpResult < 0;
            }

            return false;
        }
    }
}