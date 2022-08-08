namespace TestLevelDB.Server
{
    public sealed class ClusterServerThreadPool : ClusterServer
    {
        public override Task InitServer()
        {
            const int sensorsCnt = Program.SensorsCnt;
            const int threadCount = Program.ThreadsCount;
            const int batchSize = Program.SensorsCnt / threadCount;

            void OpenDbThread(int start, TaskCompletionSource task)
            {
                for (int i = 0; i < batchSize; ++i)
                    AddDatabase(start + i);

                task.SetResult();
            }

            var threadFinishTasks = new TaskCompletionSource[threadCount];

            for (int i = 0; i < sensorsCnt; i += batchSize)
            {
                int j = i;
                int threadId = i / batchSize;
                threadFinishTasks[threadId] = new TaskCompletionSource();

                ThreadPool.QueueUserWorkItem(_ => OpenDbThread(j, threadFinishTasks[threadId]), null);
            }

            return Task.WhenAll(threadFinishTasks.Select(u => u.Task));
        }


        public override Task AddData(Data[] data)
        {
            return AddDataThreadPool(data);
        }
    }
}
