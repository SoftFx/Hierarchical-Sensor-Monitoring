using System.Collections.Generic;

namespace HSMServer.Model.ManagementApi.SensorTree
{
    // Transport-agnostic outcome of a sensor-tree read (see SensorTreeReadService,
    // #1391): the REST controllers render a failure through the area's uniform JSON
    // error contract (FromReadFailure below), the MCP tools render the same failure
    // as tool error text — one authorization and mapping decision, two transports.
    public enum SensorTreeReadOutcome
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


    public sealed record SensorTreeReadFailure
    {
        public SensorTreeReadOutcome Outcome { get; init; }

        // Field-keyed messages of a ValidationFailed outcome.
        public IDictionary<string, string[]> Errors { get; init; }

        // Human-readable message of the Forbidden/Unavailable outcomes.
        public string Message { get; init; }
    }


    // A value-or-failure envelope: Value is set exactly when Failure is null.
    public sealed class SensorTreeReadResult<T>
    {
        private SensorTreeReadResult(T value, SensorTreeReadFailure failure)
        {
            Value = value;
            Failure = failure;
        }

        public T Value { get; }

        public SensorTreeReadFailure Failure { get; }

        public bool Success => Failure is null;

        public static SensorTreeReadResult<T> Ok(T value) => new(value, null);

        public static SensorTreeReadResult<T> Fail(SensorTreeReadOutcome outcome,
            IDictionary<string, string[]> errors = null, string message = null) =>
            new(default, new SensorTreeReadFailure { Outcome = outcome, Errors = errors, Message = message });

        // Re-wraps an existing failure under another payload type — the shared
        // sensor-resolution helper below serves both the item and the history
        // reads, which answer different DTOs on the same failure.
        public static SensorTreeReadResult<T> FromFailure(SensorTreeReadFailure failure) =>
            new(default, failure);
    }
}
