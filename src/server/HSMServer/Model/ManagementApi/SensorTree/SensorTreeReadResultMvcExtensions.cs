using Microsoft.AspNetCore.Mvc;

namespace HSMServer.Model.ManagementApi.SensorTree
{
    // The MVC rendering of the sensor-tree read envelope (#1391): success ->
    // 200 with the DTO, failure -> the area's uniform error contract. The MCP
    // tools translate the same envelope their own way and never go through
    // here; kept in its own file so SensorTreeReadResult stays free of the
    // MVC dependency the transport-agnostic service promises (#1392 review).
    public static class SensorTreeReadResultMvcExtensions
    {
        public static IActionResult ToActionResult<T>(this SensorTreeReadResult<T> result) =>
            result.Success
                ? new OkObjectResult(result.Value)
                : ManagementApiErrors.FromReadFailure(result.Failure);
    }
}
