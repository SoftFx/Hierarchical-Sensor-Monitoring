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
            var servers = new IServer[]
            {
                new SingleServer(),
                new ClusterServerThreadPool(),
            };

            foreach (var server in servers)
                await RunningContext.RunTests(server);

            RunningContext.PrintStats();

            Console.ReadLine();
            Console.WriteLine("Finish");

            foreach (var s in servers)
                await s.CloseDatabase();

            //var dbPath = Path.Combine(Environment.CurrentDirectory, "test_db");

            //if (Directory.Exists(dbPath))
            //    Directory.Delete(dbPath, true);

            //Directory.CreateDirectory(dbPath);

            //var options = new Options()
            //{
            //    CreateIfMissing = true,
            //    MaxOpenFiles = 100000,
            //    CompressionLevel = CompressionLevel.SnappyCompression,
            //    WriteBufferSize = 8 * 1024 * 1024,
            //};


            //var database = new DB(options, dbPath);

            //void DbAdd(string key, int val)
            //{
            //    var keyBytes = Encoding.UTF8.GetBytes(key);
            //    var valBytes = Encoding.UTF8.GetBytes(val.ToString());

            //    database.Put(keyBytes, valBytes);
            //}

            //DbAdd("1_23", 1);
            //DbAdd("1_24", 2);
            //DbAdd("1_30", 3);
            //DbAdd("2_01", 4);
            //DbAdd("2_10", 5);

            //var iterator = database.CreateIterator();

            //iterator.Seek("2_11");
            //iterator.Prev();

            //var value = iterator.Value();



            //Console.WriteLine(Encoding.UTF8.GetString(value));

            //database.Dispose();
        }
    }
}