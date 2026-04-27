using HSMServer.Authentication;
using HSMServer.Controllers.MCP.McpModels;
using HSMServer.Model.Authentication;
using HSMServer.ServerConfiguration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HSMServer.Controllers.MCP
{
    [ApiController]
    [Route("mcp")]
    [McpAuthorize]
    [ResponseCache(NoStore = true)]
    public class McpTransportController : ControllerBase
    {
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        private readonly IMcpToolService _toolService;
        private readonly ILogger<McpTransportController> _logger;

        public McpTransportController(IMcpToolService toolService, ILogger<McpTransportController> logger)
        {
            _toolService = toolService;
            _logger = logger;
        }

        private User CurrentUser => HttpContext.GetMcpUser();


        [HttpPost]
        public async Task Post()
        {
            string body;
            using (var reader = new StreamReader(Request.Body, Encoding.UTF8))
                body = await reader.ReadToEndAsync();

            if (string.IsNullOrWhiteSpace(body))
            {
                await WriteJsonResponse(new JsonRpcErrorResponse
                {
                    Id = null,
                    Error = new JsonRpcError { Code = -32700, Message = "Parse error: empty body" }
                });
                return;
            }

            JsonRpcRequest request;
            try
            {
                request = JsonSerializer.Deserialize<JsonRpcRequest>(body, _jsonOptions);
            }
            catch (JsonException)
            {
                await WriteJsonResponse(new JsonRpcErrorResponse
                {
                    Id = null,
                    Error = new JsonRpcError { Code = -32700, Message = "Parse error: invalid JSON" }
                });
                return;
            }

            if (request == null || request.JsonRpc != "2.0" || string.IsNullOrEmpty(request.Method))
            {
                await WriteJsonResponse(new JsonRpcErrorResponse
                {
                    Id = request?.Id,
                    Error = new JsonRpcError { Code = -32600, Message = "Invalid Request: missing jsonrpc or method" }
                });
                return;
            }

            // Notification without id — acknowledge but don't send response
            if (request.Id == null)
            {
                if (request.Method == "notifications/initialized")
                {
                    Response.StatusCode = 202;
                    return;
                }

                await WriteJsonResponse(new JsonRpcErrorResponse
                {
                    Id = null,
                    Error = new JsonRpcError { Code = -32600, Message = "Invalid Request: notifications must have no id" }
                });
                return;
            }

            var acceptSse = Request.Headers["Accept"].Any(h => h?.Contains("text/event-stream") == true);

            try
            {
                object response = request.Method switch
                {
                    "initialize" => HandleInitialize(request),
                    "tools/list" => HandleToolsList(request),
                    "tools/call" => await HandleToolsCall(request),
                    _ => new JsonRpcErrorResponse
                    {
                        Id = request.Id,
                        Error = new JsonRpcError { Code = -32601, Message = $"Method not found: {request.Method}" }
                    }
                };

                if (response is JsonRpcErrorResponse errorResponse)
                {
                    if (acceptSse)
                        await WriteSseJsonRpcMessage(errorResponse);
                    else
                        await WriteJsonResponse(errorResponse);
                    return;
                }

                var rpcResponse = new JsonRpcResponse
                {
                    Id = request.Id,
                    Result = response
                };

                if (acceptSse)
                    await WriteSseJsonRpcMessage(rpcResponse);
                else
                    await WriteJsonResponse(rpcResponse);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling MCP JSON-RPC method {Method}", request.Method);

                var errorResponse = new JsonRpcErrorResponse
                {
                    Id = request.Id,
                    Error = new JsonRpcError { Code = -32603, Message = "Internal error" }
                };

                if (acceptSse)
                    await WriteSseJsonRpcMessage(errorResponse);
                else
                    await WriteJsonResponse(errorResponse);
            }
        }

        [HttpGet]
        public async Task Get()
        {
            if (!Request.Headers["Accept"].Any(h => h?.Contains("text/event-stream") == true))
            {
                Response.StatusCode = 400;
                return;
            }

            Response.ContentType = "text/event-stream";
            Response.StatusCode = 200;
            await Response.StartAsync();

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                HttpContext.RequestAborted, timeoutCts.Token);

            try
            {
                while (!linkedCts.Token.IsCancellationRequested)
                {
                    await Task.Delay(30000, linkedCts.Token);
                    var heartbeat = ": heartbeat\n\n"u8.ToArray();
                    await Response.Body.WriteAsync(heartbeat, linkedCts.Token);
                    await Response.Body.FlushAsync(linkedCts.Token);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        [HttpDelete]
        public IActionResult Delete()
        {
            return Ok(new { status = "session terminated" });
        }


        private object HandleInitialize(JsonRpcRequest request)
        {
            return new McpInitializeResult
            {
                ProtocolVersion = "2024-11-05",
                Capabilities = new McpCapabilities(),
                ServerInfo = new McpServerInfo
                {
                    Name = ServerConfig.Name,
                    Version = ServerConfig.Version
                }
            };
        }

        private object HandleToolsList(JsonRpcRequest request)
        {
            var definitions = _toolService.GetToolDefinitions();
            var tools = definitions.Select(td => new
            {
                name = td.Name,
                description = td.Description,
                inputSchema = BuildInputSchema(td)
            }).ToList();

            return new { tools };
        }

        private async Task<object> HandleToolsCall(JsonRpcRequest request)
        {
            if (request.Params == null)
            {
                return new JsonRpcErrorResponse
                {
                    Id = request.Id,
                    Error = new JsonRpcError { Code = -32602, Message = "Invalid params: missing params" }
                };
            }

            var toolName = request.Params.Value.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
            if (string.IsNullOrEmpty(toolName))
            {
                return new JsonRpcErrorResponse
                {
                    Id = request.Id,
                    Error = new JsonRpcError { Code = -32602, Message = "Invalid params: missing tool name" }
                };
            }

            Dictionary<string, object> arguments = null;
            if (request.Params.Value.TryGetProperty("arguments", out var argsEl) && argsEl.ValueKind == JsonValueKind.Object)
            {
                arguments = new Dictionary<string, object>();
                foreach (var prop in argsEl.EnumerateObject())
                    arguments[prop.Name] = ExtractValue(prop.Value);
            }

            _logger.LogInformation("MCP JSON-RPC tool called: {Tool} by user {User}", toolName, CurrentUser?.Name);

            var result = await _toolService.ExecuteToolAsync(toolName, CurrentUser, arguments);

            return new
            {
                content = new[]
                {
                    new { type = "text", text = JsonSerializer.Serialize(result, _jsonOptions) }
                }
            };
        }


        private static object BuildInputSchema(ToolDefinition td)
        {
            if (td.InputSchema == null)
                return new { type = "object", properties = new { } };

            var schema = new Dictionary<string, object> { ["type"] = "object" };
            var properties = new Dictionary<string, object>();

            foreach (var prop in td.InputSchema.GetType().GetProperties())
            {
                var propType = prop.PropertyType;
                var propSchema = new Dictionary<string, object>();

                if (propType == typeof(string))
                    propSchema["type"] = "string";
                else if (propType == typeof(int) || propType == typeof(long))
                    propSchema["type"] = "integer";
                else if (propType == typeof(bool))
                    propSchema["type"] = "boolean";
                else if (propType == typeof(double) || propType == typeof(decimal))
                    propSchema["type"] = "number";
                else
                    propSchema["type"] = "string";

                properties[prop.Name.ToLowerInvariant()] = propSchema;
            }

            schema["properties"] = properties;

            if (td.RequiredFields is { Length: > 0 })
                schema["required"] = td.RequiredFields;

            return schema;
        }

        private static object ExtractValue(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => element.ToString()
            };
        }

        private async Task WriteJsonResponse(object response)
        {
            Response.ContentType = "application/json";
            var json = JsonSerializer.Serialize(response, _jsonOptions);
            var bytes = Encoding.UTF8.GetBytes(json);
            await Response.Body.WriteAsync(bytes);
            await Response.Body.FlushAsync();
        }

        private async Task WriteSseJsonRpcMessage(object rpcMessage)
        {
            Response.ContentType = "text/event-stream";
            Response.StatusCode = 200;
            await Response.StartAsync();

            var json = JsonSerializer.Serialize(rpcMessage, _jsonOptions);
            var sseMessage = $"event: message\ndata: {json}\n\n";
            var bytes = Encoding.UTF8.GetBytes(sseMessage);
            await Response.Body.WriteAsync(bytes);
            await Response.Body.FlushAsync();
        }
    }
}
