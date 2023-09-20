using System.Runtime.CompilerServices;

namespace HSMPingModule.Common
{
    internal class TaskResult
    {
        internal static Task<TaskResult> OkTask { get; } = Task.FromResult(Ok);

        internal static TaskResult Ok { get; } = new();


        public bool IsOk { get; }

        public string Error { get; }


        protected TaskResult()
        {
            IsOk = true;
        }

        internal TaskResult(string error)
        {
            Error = error;
        }
    }


    internal class TaskResult<T> : TaskResult
    {
        internal T Result { get; private set; }


        private TaskResult() : base() { }

        internal TaskResult(string error) : base(error) { }


        internal static TaskResult<T> GetOk(T result) => new()
        {
            Result = result
        };
    }
}