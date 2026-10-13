using System.Collections.Generic;

namespace HSMServer.Model.ManagementApi
{
    // Transport-agnostic outcome of a management-area read (see the read
    // services — SensorTreeReadService, AlertReadService, ChatsReadService,
    // FoldersReadService): the REST controllers render a failure through the
    // area's uniform JSON error contract (FromReadFailure below), the MCP
    // tools render the same failure as tool error text — one authorization
    // and mapping decision, two transports. Born as the sensor-tree read
    // envelope (#1391), adopted by the alert reads since #1393 and renamed
    // to its area-wide name (#1398).
    public enum ManagementReadOutcome
    {
        Ok,

        // Unknown OR invisible id — deliberately indistinguishable (the area's
        // anti-enumeration rule).
        NotFound,

        // Authenticated, but the evaluator's read model does not admit the caller.
        Forbidden,

        // Request-shape failure; Errors carries the field-keyed messages.
        ValidationFailed,

        // The server could not evaluate the request within its resource bounds;
        // Message names the caller-side remedy.
        Unavailable,
    }


    public sealed record ManagementReadFailure
    {
        public ManagementReadOutcome Outcome { get; init; }

        // Field-keyed messages of a ValidationFailed outcome.
        public IDictionary<string, string[]> Errors { get; init; }

        // Human-readable message of the Forbidden/Unavailable outcomes.
        public string Message { get; init; }
    }


    // A value-or-failure envelope: Value is set exactly when Failure is null.
    public sealed class ManagementReadResult<T>
    {
        private ManagementReadResult(T value, ManagementReadFailure failure)
        {
            Value = value;
            Failure = failure;
        }

        public T Value { get; }

        public ManagementReadFailure Failure { get; }

        public bool Success => Failure is null;

        public static ManagementReadResult<T> Ok(T value) => new(value, null);

        public static ManagementReadResult<T> Fail(ManagementReadOutcome outcome,
            IDictionary<string, string[]> errors = null, string message = null) =>
            new(default, new ManagementReadFailure { Outcome = outcome, Errors = errors, Message = message });

        // Re-wraps an existing failure under another payload type — the shared
        // sensor-resolution helpers of the read services serve both the item
        // and the history/list reads, which answer different DTOs on the same
        // failure.
        public static ManagementReadResult<T> FromFailure(ManagementReadFailure failure) =>
            new(default, failure);
    }
}
