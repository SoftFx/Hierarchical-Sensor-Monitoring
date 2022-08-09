namespace TestLevelDB.Server
{
    public sealed class SingleServerThreadPool : SingleServer
    {
        public override Task InitServer()
        {
            void InitServer(TaskCompletionSource source)
            {
                AddDatabase();

                source.SetResult();
            }

            var source = new TaskCompletionSource();

            ThreadPool.QueueUserWorkItem(_ => InitServer(source));

            return source.Task;
        }


        public override Task AddData(Data[] data)
        {
            return AddDataThreadPool(data);
        }
    }
}
