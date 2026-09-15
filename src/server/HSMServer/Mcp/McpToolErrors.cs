using System.Linq;
using HSMServer.Model.ManagementApi;
using HSMServer.Model.ManagementApi.SensorTree;
using ModelContextProtocol;

namespace HSMServer.Mcp
{
    // The failure rendering shared by the tool classes (#1393 review taste:
    // the switch was duplicated between the sensor-tree and alert tools). The
    // shared read service's failure becomes a tool error — isError=true with
    // the message, the MCP rendering of what REST answers through
    // ManagementApiErrors.FromReadFailure.
    internal static class McpToolErrors
    {
        // Validation messages flatten into one text with their field keys
        // preserved (the field-keyed JSON details shape has no MCP equivalent
        // an agent consumes better). The 404 text is the area's shared
        // constant: unknown and invisible must answer the SAME string, and a
        // private copy could drift from it silently.
        public static T Unwrap<T>(SensorTreeReadResult<T> result) =>
            result.Failure is { } failure
                ? throw new McpException(failure.Outcome switch
                {
                    SensorTreeReadOutcome.ValidationFailed => failure.Errors is { Count: > 0 } errors
                        ? string.Join(" ", errors.Select(pair => $"{pair.Key}: {string.Join("; ", pair.Value)}"))
                        : "The request is invalid.",
                    SensorTreeReadOutcome.Forbidden or SensorTreeReadOutcome.Unavailable => failure.Message,
                    _ => ManagementApiErrors.NotFoundMessage,
                })
                : result.Value;
    }
}
