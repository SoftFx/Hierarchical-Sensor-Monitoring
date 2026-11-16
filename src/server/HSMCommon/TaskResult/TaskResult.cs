using System;

namespace HSMCommon.TaskResult
{
    public class TaskResult
    {
        public static TaskResult Ok { get; } = new TaskResult();

        public string Error { get; }
        public bool IsOk => string.IsNullOrEmpty(Error);

        // True when the error is a fault of the server (an exception, a failed write to the
        // store), not a refusal of the request (#1756). A refusal is the same on every
        // repeat of the request; a fault may pass. Additive: IsOk and Error are what they
        // are for any other error, so a caller that does not ask sees no difference.
        public bool IsFault { get; }

        protected TaskResult() { Error = null; }
        protected TaskResult(string error) => Error = error ?? throw new ArgumentNullException(nameof(error));

        private TaskResult(string error, bool isFault) : this(error) => IsFault = isFault;

        public static TaskResult FromError(string error) => new TaskResult(error);

        public static TaskResult FromFault(string error) => new TaskResult(error, isFault: true);
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