using HSMServer.Controllers.MCP.McpModels;
using HSMServer.Model.Authentication;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HSMServer.Controllers.MCP
{
    public interface IMcpToolService
    {
        object ListSensors(User user, Dictionary<string, object> args);
        object GetSensor(User user, Dictionary<string, object> args);
        Task<object> GetSensorHistory(User user, Dictionary<string, object> args);
        object ListProducts(User user);
        object ListSchedules();
        object GetSchedule(Dictionary<string, object> args);
        object CreateSchedule(User user, Dictionary<string, object> args);
        object UpdateSchedule(User user, Dictionary<string, object> args);
        object DeleteSchedule(User user, Dictionary<string, object> args);
        object ListAlerts(User user, Dictionary<string, object> args);
        object GetAlert(User user, Dictionary<string, object> args);
        Task<object> CreateAlert(User user, Dictionary<string, object> args);
        Task<object> UpdateAlert(User user, Dictionary<string, object> args);
        Task<object> DeleteAlert(User user, Dictionary<string, object> args);

        object ExecuteTool(string toolName, User user, Dictionary<string, object> args);
        Task<object> ExecuteToolAsync(string toolName, User user, Dictionary<string, object> args);

        List<ToolDefinition> GetToolDefinitions();
    }
}
