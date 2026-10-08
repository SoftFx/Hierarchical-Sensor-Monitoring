using Microsoft.AspNetCore.Mvc;

namespace HSMServer.Model.ManagementApi.Alerts
{
    // Transport-agnostic outcome of an alert-administration WRITE (#1500) — the
    // write-side twin of SensorTreeReadResult: the REST controllers render a
    // failure through the area's uniform JSON error contract (ToActionResult
    // below), and the future MCP write tools render the same failure as tool
    // error text. One authorization and validation decision, two transports.
    public enum PolicyWriteOutcome
    {
        Ok,

        // Unknown OR invisible id — deliberately indistinguishable (the area's
        // anti-enumeration rule).
        NotFound,

        // Authenticated and the target is in sight, but the caller may not write
        // there (read-only token or the owner's Viewer role at the boundary).
        Forbidden,

        // The request is well-formed and bound but contradicts server-side state:
        // a condition property the sensor's type does not offer, an operation the
        // property does not support, an unparseable or missing target, a chat not
        // available on the node, an unresolvable schedule reference, a TTL
        // interval/inherit contradiction. Errors carries the field-keyed messages.
        Invalid,

        // Request-shape/structural validation failure of a PRE-#1500 write
        // surface whose shipped contract answers 400 (the templates CRUD).
        // Append-only addition: post-#1500 surfaces keep using Invalid (422);
        // Errors carries the field-keyed messages like a 422.
        Validation,

        // The write conflicts with server-side state in a way validation cannot
        // see: a template-owned policy edited beyond its disable toggle, a
        // template-owned policy deletion, or the cache rejecting the full-list
        // update. Message carries the human summary.
        Conflict,
    }


    public sealed record PolicyWriteFailure
    {
        public PolicyWriteOutcome Outcome { get; init; }

        // Field-keyed messages of the Invalid/Validation outcomes (the 422/400
        // details map).
        public System.Collections.Generic.IDictionary<string, string[]> Errors { get; init; }

        // Human-readable message of the Forbidden/Conflict outcomes.
        public string Message { get; init; }

        // Optional resource pointers of a Conflict resolution (e.g.
        // {"templateId": "<id>"} when the templates create-path partial-apply
        // conflict leaves a persisted template the caller must PUT to fix) —
        // rendered into the 409 details by ToActionResult.
        public object Details { get; init; }
    }


    // A value-or-failure envelope: Value is set exactly when Failure is null.
    // Delete flows use the non-generic form (no payload on success).
    public sealed class PolicyWriteResult<T>
    {
        private PolicyWriteResult(T value, PolicyWriteFailure failure)
        {
            Value = value;
            Failure = failure;
        }

        public T Value { get; }

        public PolicyWriteFailure Failure { get; }

        public bool Success => Failure is null;

        public static PolicyWriteResult<T> Ok(T value) => new(value, null);

        public static PolicyWriteResult<T> Fail(PolicyWriteOutcome outcome,
            System.Collections.Generic.IDictionary<string, string[]> errors = null, string message = null,
            object details = null) =>
            new(default, new PolicyWriteFailure { Outcome = outcome, Errors = errors, Message = message, Details = details });
    }

    public sealed class PolicyWriteResult
    {
        private PolicyWriteResult(PolicyWriteFailure failure) => Failure = failure;

        public PolicyWriteFailure Failure { get; }

        public bool Success => Failure is null;

        public static PolicyWriteResult Ok() => new(null);

        public static PolicyWriteResult Fail(PolicyWriteOutcome outcome,
            System.Collections.Generic.IDictionary<string, string[]> errors = null, string message = null,
            object details = null) =>
            new(new PolicyWriteFailure { Outcome = outcome, Errors = errors, Message = message, Details = details });


        // The REST half of the transport split: renders the failure through the
        // area's uniform error contract. The 403 message is the same explicit
        // string the templates controller uses — never Forbid(), which would
        // engage the cookie scheme's redirect handling.
        public static IActionResult ToActionResult(PolicyWriteFailure failure) =>
            failure?.Outcome switch
            {
                PolicyWriteOutcome.Forbidden => ManagementApiErrors.Forbidden(
                    failure.Message ?? "The token is read-only or the token's owner cannot write at this target."),
                PolicyWriteOutcome.Invalid => ManagementApiErrors.UnprocessableEntity(failure.Errors),
                PolicyWriteOutcome.Validation => ManagementApiErrors.Validation(failure.Errors),
                PolicyWriteOutcome.Conflict when failure.Details is not null => ManagementApiErrors.Conflict(
                    failure.Message ?? "The write conflicts with the current state of the resource.", failure.Details),
                PolicyWriteOutcome.Conflict => ManagementApiErrors.Conflict(
                    failure.Message ?? "The write conflicts with the current state of the resource."),
                _ => ManagementApiErrors.NotFound(),
            };
    }

    public static class PolicyWriteResultMvcExtensions
    {
        // Value-carrying results render the SAME failure set, with the payload
        // answering success responses (creates use CreatedAtAction directly so
        // the Location header carries the SERVER-generated id).
        public static IActionResult ToActionResult<T>(this PolicyWriteResult<T> result) =>
            result.Success
                ? new OkObjectResult(result.Value)
                : PolicyWriteResult.ToActionResult(result.Failure);
    }
}
