using System.Text.Json;
using System.Text.Json.Serialization;

namespace HSMServer.Controllers.MCP.McpModels
{
    public class JsonRpcRequest
    {
        [JsonPropertyName("jsonrpc")]
        public string JsonRpc { get; set; }

        [JsonPropertyName("id")]
        public object Id { get; set; }

        [JsonPropertyName("method")]
        public string Method { get; set; }

        [JsonPropertyName("params")]
        public JsonElement? Params { get; set; }
    }

    public class JsonRpcResponse
    {
        [JsonPropertyName("jsonrpc")]
        public string JsonRpc { get; set; } = "2.0";

        [JsonPropertyName("id")]
        public object Id { get; set; }

        [JsonPropertyName("result")]
        public object Result { get; set; }
    }

    public class JsonRpcErrorResponse
    {
        [JsonPropertyName("jsonrpc")]
        public string JsonRpc { get; set; } = "2.0";

        [JsonPropertyName("id")]
        public object Id { get; set; }

        [JsonPropertyName("error")]
        public JsonRpcError Error { get; set; }
    }

    public class JsonRpcError
    {
        [JsonPropertyName("code")]
        public int Code { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; }

        [JsonPropertyName("data")]
        public object Data { get; set; }
    }

    public class McpInitializeResult
    {
        [JsonPropertyName("protocolVersion")]
        public string ProtocolVersion { get; set; }

        [JsonPropertyName("capabilities")]
        public McpCapabilities Capabilities { get; set; }

        [JsonPropertyName("serverInfo")]
        public McpServerInfo ServerInfo { get; set; }
    }

    public class McpCapabilities
    {
        [JsonPropertyName("tools")]
        public object Tools { get; set; } = new { };
    }

    public class McpServerInfo
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("version")]
        public string Version { get; set; }
    }
}
