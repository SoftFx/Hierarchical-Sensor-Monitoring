namespace HSMServer.Controllers.MCP
{
    public record ToolDefinition(string Name, string Description, object InputSchema, string[] RequiredFields = null);
}
