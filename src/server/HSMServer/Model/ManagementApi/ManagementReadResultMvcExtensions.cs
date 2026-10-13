using Microsoft.AspNetCore.Mvc;

namespace HSMServer.Model.ManagementApi
{
    // The MVC rendering of the management read envelope: success -> 200 with
    // the DTO, failure -> the area's uniform error contract. The MCP tools
    // translate the same envelope their own way and never go through here;
    // kept in its own file so ManagementReadResult stays free of the MVC
    // dependency the transport-agnostic services promise (#1392 review).
    public static class ManagementReadResultMvcExtensions
    {
        public static IActionResult ToActionResult<T>(this ManagementReadResult<T> result) =>
            result.Success
                ? new OkObjectResult(result.Value)
                : ManagementApiErrors.FromReadFailure(result.Failure);
    }
}
