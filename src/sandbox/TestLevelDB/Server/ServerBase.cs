using LevelDB;

namespace TestLevelDB.Server
{
    public abstract class ServerBase
    {
        protected readonly Options _options = new()
        {
            CreateIfMissing = true,
            MaxOpenFiles = 100000,
            CompressionLevel = CompressionLevel.SnappyCompression,
        };


        public string Type { get; }


        public ServerBase()
        {
            Type = GetType().Name;
        }


        public virtual Task AddData(Data[] data)
        {
            foreach (var item in data)
                AddDataToSensor(item);

            return Task.CompletedTask;
        }


        protected abstract void AddDataToSensor(Data item);

        protected abstract void AddDatabase(int sensorId = 0);


        protected Task AddDataThreadPool(Data[] data)
        {
            const int threadCount = Program.ThreadsCount;
            const int batchSize = Program.TotalDataCnt / threadCount;

            void AddData(ArraySegment<Data> data, TaskCompletionSource source)
            {
                foreach (var item in data)
                    AddDataToSensor(item);

                source.SetResult();
            }

            var threadFinishTasks = new TaskCompletionSource[threadCount];

            for (int i = 0; i < threadCount; i++)
            {
                int j = i;
                threadFinishTasks[j] = new TaskCompletionSource();

                var segment = new ArraySegment<Data>(data, j * batchSize, batchSize);

                ThreadPool.QueueUserWorkItem(_ => AddData(segment, threadFinishTasks[j]), null);
            }

            return Task.WhenAll(threadFinishTasks.Select(u => u.Task));
        }
    }
}
