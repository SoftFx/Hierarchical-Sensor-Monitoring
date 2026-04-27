using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using HSMServer.Authentication;
using HSMServer.Model.Authentication;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace HSMServer.Controllers.MCP
{
    [ApiController]
    [Route("api/mcp/v1")]
    [McpAuthorize]
    public class MCPServerController : ControllerBase
    {
        private readonly IMcpToolService _toolService;
        private readonly ILogger<MCPServerController> _logger;

        public MCPServerController(IMcpToolService toolService, ILogger<MCPServerController> logger)
        {
            _toolService = toolService;
            _logger = logger;
        }

        private User CurrentUser => HttpContext.GetMcpUser();

        [HttpGet("health")]
        [AllowAnonymous]
        public IActionResult Health()
        {
            return Ok(new { status = "healthy", timestamp = DateTime.UtcNow });
        }

        [HttpGet("tools")]
        public IActionResult GetTools()
        {
            var tools = _toolService.GetToolDefinitions();
            return Ok(new { tools });
        }

        [HttpPost("tools/call")]
        public async Task<IActionResult> CallTool([FromBody] McpToolRequest request)
        {
            if (request?.Tool == null)
                return BadRequest(new { error = "Tool name is required" });

            _logger.LogInformation("MCP tool called: {Tool} by user {User}", request.Tool, CurrentUser?.Name);

            try
            {
                var result = await _toolService.ExecuteToolAsync(request.Tool, CurrentUser, request.Arguments);
                return Ok(new { result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing MCP tool {Tool}", request.Tool);
                return StatusCode(500, new { error = "Internal server error" });
            }
        }
    }
}
