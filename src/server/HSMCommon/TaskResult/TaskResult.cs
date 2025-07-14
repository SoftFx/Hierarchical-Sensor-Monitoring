using System;

namespace HSMCommon.TaskResult
{
    public class TaskResult
    {
        public static TaskResult Ok { get; } = new TaskResult();

        public string Error { get; }
        public bool IsOk => string.IsNullOrEmpty(Error);

        protected TaskResult() { Error = null; }
        protected TaskResult(string error) => Error = error ?? throw new ArgumentNullException(nameof(error));

        public static TaskResult FromError(string error) => new TaskResult(error);
    }

    public sealed class TaskResult<T> : TaskResult
    {
        public T Value { get; }

        private TaskResult(T value) : base()
        {
            Value = value;
        }

        private TaskResult(string error) : base(error) { }

        public static TaskResult<T> FromValue(T value) => new TaskResult<T>(value);
        public static new TaskResult<T> FromError(string error) => new TaskResult<T>(error);

        public void Deconstruct(out T value, out string error)
        {
            value = Value;
            error = Error;
        }
    }
}