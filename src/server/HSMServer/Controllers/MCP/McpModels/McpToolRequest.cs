using System.Collections.Generic;

namespace HSMServer.Controllers.MCP
{
    public class McpToolRequest
    {
        public string Tool { get; set; }
        public Dictionary<string, object> Arguments { get; set; }
    }
}
