namespace HSMCommon.TaskResult
{
    namespace HSMCommon.TaskResult
    {
        public class TaskResult
        {
            public static TaskResult Ok { get; } = new();


            public string Error { get; protected set; }


            public bool IsOk => string.IsNullOrEmpty(Error);


            protected TaskResult() { }

            public TaskResult(string error)
            {
                Error = error;
            }
        }


        public class TaskResult<T> : TaskResult
        {
            public T Value { get; private set; }


            private TaskResult(string error) : base(error) { }

            public TaskResult(T value)
            {
                Value = value;
            }


            public static TaskResult<T> AsError(string error) => new(error);
        }
    }
}
