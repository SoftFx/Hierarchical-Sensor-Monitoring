using System.Linq;
using HSMServer.Model.ManagementApi;
using HSMServer.Model.ManagementApi.Alerts;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace HSMServer.Mcp
{
    // The write-side twin of McpToolErrors: renders a PolicyWriteFailure as
    // tool error text (an McpException the SDK relays as isError) with the
    // SAME strings the REST arm produces in PolicyWriteResult.ToActionResult —
    // one decision, two transports (#1500's design, carried into MCP).
    internal static class McpToolWriteErrors
    {
        public static T Unwrap<T>(PolicyWriteResult<T> result) =>
            result.Success ? result.Value : Throw<T>(result.Failure);

        // Delete flows carry no payload; the tool composes its own success
        // record and only the failure path throws.
        public static void Unwrap(PolicyWriteResult result)
        {
            if (!result.Success)
                Throw<object>(result.Failure);
        }


        private static T Throw<T>(PolicyWriteFailure failure) => throw new McpException(MessageOf(failure));

        private static string MessageOf(PolicyWriteFailure failure)
        {
            switch (failure.Outcome)
            {
                // Field-keyed messages flatten with their keys — the agent
                // corrects the right argument (the McpToolErrors
                // ValidationFailed arm's shape).
                case PolicyWriteOutcome.Validation:
                case PolicyWriteOutcome.Invalid:
                    return failure.Errors is { Count: > 0 }
                        ? string.Join(" ", failure.Errors.Select(pair => $"{pair.Key}: {string.Join("; ", pair.Value)}"))
                        : "The request is invalid.";

                case PolicyWriteOutcome.Forbidden:
                    return failure.Message ?? PolicyWriteResult.WriteDeniedMessage;

                case PolicyWriteOutcome.Conflict:
                {
                    var message = failure.Message ?? "The write conflicts with the current state of the resource.";

                    // REST carries conflict resolution pointers in the 409
                    // details (e.g. the templates create-path templateId);
                    // the tool error text must disclose them too.
                    return failure.Details is { Count: > 0 } pointers
                        ? $"{message} {string.Join(" ", pointers.Select(pair => $"{pair.Key}: {pair.Value}"))}"
                        : message;
                }

                default:
                    return ManagementApiErrors.NotFoundMessage;
            }
        }
    }
}
