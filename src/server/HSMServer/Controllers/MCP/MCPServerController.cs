using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Schedule;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.TableOfChanges;
using HSMServer.Extensions;
using HSMServer.Model.Authentication;
using HSMServer.Model.TreeViewModel;
using HSMServer.Model.DataAlerts;
using HSMCommon.Model;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using SensorStatus = HSMServer.Model.TreeViewModel.SensorStatus;

namespace HSMServer.Controllers.MCP
{
    [ApiController]
    [Route("api/mcp/v1")]
    [McpAuthorize]
    public class MCPServerController : ControllerBase
    {
        private readonly ITreeValuesCache _cache;
        private readonly TreeViewModel _tree;
        private readonly IAlertScheduleProvider _scheduleProvider;
        private readonly IUserManager _userManager;

        public MCPServerController(ITreeValuesCache cache, TreeViewModel tree, IAlertScheduleProvider scheduleProvider, IUserManager userManager)
        {
            _cache = cache;
            _tree = tree;
            _scheduleProvider = scheduleProvider;
            _userManager = userManager;
        }

        private User CurrentUser => HttpContext.GetMcpUser();

        private IEnumerable<Guid> CurrentUserProductIds
        {
            get
            {
                var user = CurrentUser;
                if (user == null)
                    return Enumerable.Empty<Guid>();

                return user.ProductsRoles.Select(p => p.Item1);
            }
        }

        private bool HasAccessToProduct(Guid productId)
        {
            var user = CurrentUser;
            if (user == null)
                return false;

            return user.IsProductAvailable(productId) || user.IsAdmin;
        }

        [HttpGet("health")]
        [AllowAnonymous]
        public IActionResult Health()
        {
            return Ok(new { status = "healthy", timestamp = DateTime.UtcNow });
        }

        [HttpGet("tools")]
        public IActionResult GetTools()
        {
            var tools = new[]
            {
                new ToolDefinition("list_sensors", "List all sensors with optional filtering", new { productId = "", status = "" }),
                new ToolDefinition("get_sensor", "Get sensor details by ID", new { sensorId = "" }),
                new ToolDefinition("get_sensor_history", "Get sensor value history", new { sensorId = "", from = "", to = "" }),
                new ToolDefinition("list_products", "List all products/folders", null),
                new ToolDefinition("list_schedules", "List all alert schedules", null),
                new ToolDefinition("get_schedule", "Get schedule details", new { scheduleId = "" }),
                new ToolDefinition("create_schedule", "Create a new alert schedule", new CreateScheduleInput()),
                new ToolDefinition("update_schedule", "Update an existing alert schedule", new UpdateScheduleInput()),
                new ToolDefinition("delete_schedule", "Delete an alert schedule", new { scheduleId = "" }),
                new ToolDefinition("list_alerts", "List all alerts for a sensor", new { sensorId = "" }),
                new ToolDefinition("get_alert", "Get alert details", new { alertId = "" }),
                new ToolDefinition("create_alert", "Create a new alert for a sensor", new CreateAlertInput()),
                new ToolDefinition("update_alert", "Update an existing alert", new UpdateAlertInput()),
                new ToolDefinition("delete_alert", "Delete an alert", new { alertId = "" })
            };
            return Ok(new { tools });
        }

        [HttpPost("tools/call")]
        public async Task<IActionResult> CallTool([FromBody] McpToolRequest request)
        {
            try
            {
                object result = request.Tool switch
                {
                    "list_sensors" => ListSensors(request.Arguments),
                    "get_sensor" => GetSensor(request.Arguments),
                    "get_sensor_history" => await GetSensorHistory(request.Arguments),
                    "list_products" => ListProducts(),
                    "list_schedules" => ListSchedules(),
                    "get_schedule" => GetSchedule(request.Arguments),
                    "create_schedule" => CreateSchedule(request.Arguments),
                    "update_schedule" => UpdateSchedule(request.Arguments),
                    "delete_schedule" => DeleteSchedule(request.Arguments),
                    "list_alerts" => ListAlerts(request.Arguments),
                    "get_alert" => GetAlert(request.Arguments),
                    "create_alert" => await CreateAlert(request.Arguments),
                    "update_alert" => await UpdateAlert(request.Arguments),
                    "delete_alert" => await DeleteAlert(request.Arguments),
                    _ => new { error = $"Unknown tool: {request.Tool}" }
                };

                return Ok(new { result });
            }
            catch (Exception ex)
            {
                return Ok(new { error = ex.Message, stack = ex.StackTrace });
            }
        }

        private object ListSensors(Dictionary<string, object> args)
        {
            var status = args?.GetValueOrDefault("status")?.ToString();

            var sensors = _tree.Sensors.Values
                .Where(s => HasAccessToProduct(s.RootProduct?.Id ?? Guid.Empty))
                .ToList();

            if (!string.IsNullOrEmpty(status) && Enum.TryParse<SensorStatus>(status, true, out var sensorStatus))
                sensors = sensors.Where(s => s.Status == sensorStatus).ToList();

            return sensors.Select(s => new
            {
                id = s.Id,
                name = s.Name,
                path = s.Path,
                type = s.Type.ToString(),
                status = s.Status.ToString(),
                lastValue = s.LastValue?.ShortInfo,
                lastUpdateTime = s.UpdateTime
            }).ToList();
        }

        private object GetSensor(Dictionary<string, object> args)
        {
            var sensorId = args?["sensorId"]?.ToString();
            if (string.IsNullOrEmpty(sensorId))
                return new { error = "sensorId is required" };

            if (!Guid.TryParse(sensorId, out var guid))
                return new { error = "Invalid sensorId format" };

            if (!_tree.Sensors.TryGetValue(guid, out var sensor))
                return new { error = "Sensor not found" };

            if (!HasAccessToProduct(sensor.RootProduct?.Id ?? Guid.Empty))
                return new { error = "Access denied: sensor belongs to different product" };

            return new
            {
                id = sensor.Id,
                name = sensor.Name,
                path = sensor.Path,
                type = sensor.Type.ToString(),
                status = sensor.Status.ToString(),
                lastValue = sensor.LastValue?.ShortInfo,
                lastUpdateTime = sensor.UpdateTime
            };
        }

        private async Task<object> GetSensorHistory(Dictionary<string, object> args)
        {
            var sensorId = args?["sensorId"]?.ToString();
            var fromStr = args?["from"]?.ToString();
            var toStr = args?["to"]?.ToString();

            if (string.IsNullOrEmpty(sensorId))
                return new { error = "sensorId is required" };

            if (!Guid.TryParse(sensorId, out var sensorGuid))
                return new { error = "Invalid sensorId format" };

            if (!_tree.Sensors.TryGetValue(sensorGuid, out var sensor))
                return new { error = "Sensor not found" };

            if (!HasAccessToProduct(sensor.RootProduct?.Id ?? Guid.Empty))
                return new { error = "Access denied: sensor belongs to different product" };

            var from = string.IsNullOrEmpty(fromStr) ? DateTime.UtcNow.AddDays(-1) : DateTime.Parse(fromStr);
            var to = string.IsNullOrEmpty(toStr) ? DateTime.UtcNow : DateTime.Parse(toStr);

            var values = new List<object>();
            await foreach (var batch in _cache.GetSensorValuesPage(sensorGuid, from, to, 1000))
            {
                foreach (var v in batch)
                {
                    values.Add(new
                    {
                        time = v.GetType().GetProperty("Time")?.GetValue(v),
                        status = v.Status.ToString()
                    });
                }
            }

            return values;
        }

        private object ListProducts()
        {
            var products = _tree.Nodes.Values.Where(p => HasAccessToProduct(p.Id));

            return products.Select(p => new
            {
                id = p.Id,
                name = p.Name,
                path = p.Path,
                sensorCount = p.AllSensorsCount
            }).ToList();
        }

        private object ListSchedules()
        {
            return _scheduleProvider.GetAllSchedules().Select(s => new
            {
                id = s.Id,
                name = s.Name,
                timezone = s.Timezone,
                schedule = s.Schedule
            }).ToList();
        }

        private object GetSchedule(Dictionary<string, object> args)
        {
            var scheduleId = args?["scheduleId"]?.ToString();
            if (string.IsNullOrEmpty(scheduleId))
                return new { error = "scheduleId is required" };

            if (!Guid.TryParse(scheduleId, out var scheduleGuid))
                return new { error = "Invalid scheduleId format" };

            var schedule = _scheduleProvider.GetSchedule(scheduleGuid);
            if (schedule == null)
                return new { error = "Schedule not found" };

            return new
            {
                id = schedule.Id,
                name = schedule.Name,
                timezone = schedule.Timezone,
                schedule = schedule.Schedule
            };
        }

        private object CreateSchedule(Dictionary<string, object> args)
        {
            var name = args?["name"]?.ToString();
            var timezone = args?["timezone"]?.ToString() ?? "UTC";
            var schedule = args?["schedule"]?.ToString();

            if (string.IsNullOrEmpty(name))
                return new { error = "name is required" };

            if (string.IsNullOrEmpty(schedule))
                return new { error = "schedule is required" };

            var newSchedule = new AlertSchedule
            {
                Id = Guid.NewGuid(),
                Name = name,
                Timezone = timezone,
                Schedule = schedule
            };

            _scheduleProvider.SaveSchedule(newSchedule);

            return new
            {
                id = newSchedule.Id,
                name = newSchedule.Name,
                timezone = newSchedule.Timezone,
                schedule = newSchedule.Schedule,
                message = "Schedule created successfully"
            };
        }

        private object UpdateSchedule(Dictionary<string, object> args)
        {
            var scheduleId = args?["scheduleId"]?.ToString();
            var name = args?["name"]?.ToString();
            var timezone = args?["timezone"]?.ToString();
            var schedule = args?["schedule"]?.ToString();

            if (string.IsNullOrEmpty(scheduleId))
                return new { error = "scheduleId is required" };

            if (!Guid.TryParse(scheduleId, out var scheduleGuid))
                return new { error = "Invalid scheduleId format" };

            var existingSchedule = _scheduleProvider.GetSchedule(scheduleGuid);
            if (existingSchedule == null)
                return new { error = "Schedule not found" };

            if (!string.IsNullOrEmpty(name))
                existingSchedule.Name = name;
            if (!string.IsNullOrEmpty(timezone))
                existingSchedule.Timezone = timezone;
            if (!string.IsNullOrEmpty(schedule))
                existingSchedule.Schedule = schedule;

            _scheduleProvider.SaveSchedule(existingSchedule);

            return new
            {
                id = existingSchedule.Id,
                name = existingSchedule.Name,
                timezone = existingSchedule.Timezone,
                schedule = existingSchedule.Schedule,
                message = "Schedule updated successfully"
            };
        }

        private object DeleteSchedule(Dictionary<string, object> args)
        {
            var scheduleId = args?["scheduleId"]?.ToString();

            if (string.IsNullOrEmpty(scheduleId))
                return new { error = "scheduleId is required" };

            if (!Guid.TryParse(scheduleId, out var scheduleGuid))
                return new { error = "Invalid scheduleId format" };

            var existingSchedule = _scheduleProvider.GetSchedule(scheduleGuid);
            if (existingSchedule == null)
                return new { error = "Schedule not found" };

            _scheduleProvider.DeleteSchedule(scheduleGuid);

            return new { message = "Schedule deleted successfully" };
        }

        private object ListAlerts(Dictionary<string, object> args)
        {
            var sensorId = args?.GetValueOrDefault("sensorId")?.ToString();
            
            var alerts = new List<object>();
            
            var sensors = _tree.Sensors.Values.Where(s => HasAccessToProduct(s.RootProduct?.Id ?? Guid.Empty));

            foreach (var sensor in sensors)
            {
                if (!string.IsNullOrEmpty(sensorId) && sensor.Id.ToString() != sensorId)
                    continue;
                    
                if (sensor.DataAlerts != null)
                {
                    foreach (var alertList in sensor.DataAlerts.Values)
                    {
                        foreach (var alert in alertList)
                        {
                            alerts.Add(new
                            {
                                id = alert.Id,
                                name = GetAlertDisplayName(alert),
                                sensorId = sensor.Id,
                                isEnabled = !alert.IsDisabled
                            });
                        }
                    }
                }
            }

            return alerts;
        }

        private object GetAlert(Dictionary<string, object> args)
        {
            var alertId = args?["alertId"]?.ToString();
            if (string.IsNullOrEmpty(alertId))
                return new { error = "alertId is required" };

            if (!Guid.TryParse(alertId, out var alertGuid))
                return new { error = "Invalid alertId format" };

            var sensors = _tree.Sensors.Values.Where(s => HasAccessToProduct(s.RootProduct?.Id ?? Guid.Empty));

            foreach (var sensor in sensors)
            {
                if (sensor.DataAlerts != null)
                {
                    foreach (var alertList in sensor.DataAlerts.Values)
                    {
                        var alert = alertList.FirstOrDefault(a => a.Id == alertGuid);
                        if (alert != null)
                        {
                            return new
                            {
                                id = alert.Id,
                                name = GetAlertDisplayName(alert),
                                sensorId = sensor.Id,
                                isEnabled = !alert.IsDisabled
                            };
                        }
                    }
                }
            }

            return new { error = "Alert not found" };
        }

        private async Task<object> CreateAlert(Dictionary<string, object> args)
        {
            var sensorId = args?["sensorId"]?.ToString();
            if (string.IsNullOrEmpty(sensorId))
                return new { error = "sensorId is required" };

            if (!Guid.TryParse(sensorId, out var sensorGuid))
                return new { error = "Invalid sensorId format" };

            if (!_tree.Sensors.TryGetValue(sensorGuid, out var sensor))
                return new { error = "Sensor not found" };

            if (!HasAccessToProduct(sensor.RootProduct?.Id ?? Guid.Empty))
                return new { error = "Access denied: sensor belongs to different product" };

            var alertScheduleId = ParseGuid(args?.GetValueOrDefault("alertScheduleId")?.ToString());

            var policyUpdate = BuildFullPolicyUpdate(args, Guid.NewGuid(), alertScheduleId);

            var update = new SensorUpdate
            {
                Id = sensorGuid,
                Policies = new List<PolicyUpdate> { policyUpdate }
            };

            var result = await _cache.UpdateSensorAsync(update);

            return new
            {
                id = policyUpdate.Id,
                sensorId = sensorGuid,
                message = result.IsOk ? "Alert created successfully" : $"Error: {result.Error}"
            };
        }

        private async Task<object> UpdateAlert(Dictionary<string, object> args)
        {
            var alertId = args?["alertId"]?.ToString();
            if (string.IsNullOrEmpty(alertId))
                return new { error = "alertId is required" };

            if (!Guid.TryParse(alertId, out var alertGuid))
                return new { error = "Invalid alertId format" };

            var sensors = _tree.Sensors.Values.Where(s => HasAccessToProduct(s.RootProduct?.Id ?? Guid.Empty));

            foreach (var sensor in sensors)
            {
                if (sensor.DataAlerts != null)
                {
                    foreach (var alertList in sensor.DataAlerts.Values)
                    {
                        var alert = alertList.FirstOrDefault(a => a.Id == alertGuid);
                        if (alert != null)
                        {
                            var alertScheduleId = args?.ContainsKey("alertScheduleId") == true 
                                ? ParseGuid(args?.GetValueOrDefault("alertScheduleId")?.ToString())
                                : null;

                            var policyUpdate = BuildFullPolicyUpdate(args, alertGuid, alertScheduleId);

                            var update = new SensorUpdate
                            {
                                Id = sensor.Id,
                                Policies = new List<PolicyUpdate> { policyUpdate }
                            };

                            var result = await _cache.UpdateSensorAsync(update);

                            return new
                            {
                                id = alertGuid,
                                message = result.IsOk ? "Alert updated successfully" : $"Error: {result.Error}"
                            };
                        }
                    }
                }
            }

            return new { error = "Alert not found" };
        }

        private async Task<object> DeleteAlert(Dictionary<string, object> args)
        {
            var alertId = args?["alertId"]?.ToString();

            if (string.IsNullOrEmpty(alertId))
                return new { error = "alertId is required" };

            if (!Guid.TryParse(alertId, out var alertGuid))
                return new { error = "Invalid alertId format" };

            var sensors = _tree.Sensors.Values.Where(s => HasAccessToProduct(s.RootProduct?.Id ?? Guid.Empty));

            foreach (var sensor in sensors)
            {
                if (sensor.DataAlerts != null)
                {
                    foreach (var alertList in sensor.DataAlerts.Values)
                    {
                        var alert = alertList.FirstOrDefault(a => a.Id == alertGuid);
                        if (alert != null)
                        {
                            var policyUpdate = new PolicyUpdate
                            {
                                Id = alertGuid,
                                IsDisabled = true,
                                Initiator = InitiatorInfo.AsSystemInfo("MCP")
                            };

                            var update = new SensorUpdate
                            {
                                Id = sensor.Id,
                                Policies = new List<PolicyUpdate> { policyUpdate }
                            };

                            var result = await _cache.UpdateSensorAsync(update);

                            return new
                            {
                                id = alertGuid,
                                message = result.IsOk ? "Alert deleted successfully" : $"Error: {result.Error}"
                            };
                        }
                    }
                }
            }

            return new { error = "Alert not found" };
        }

        private static PolicyUpdate BuildFullPolicyUpdate(Dictionary<string, object> args, Guid policyId, Guid? alertScheduleId)
        {
            var conditions = BuildConditions(args);
            var schedule = BuildPolicySchedule(args);
            var destination = BuildDestination(args);
            var status = BuildStatus(args);

            long? confirmationPeriod = null;
            var confirmationPeriodStr = args?.GetValueOrDefault("confirmationPeriod")?.ToString();
            if (!string.IsNullOrEmpty(confirmationPeriodStr) && long.TryParse(confirmationPeriodStr, out var cp))
                confirmationPeriod = cp;

            return new PolicyUpdate
            {
                Id = policyId,
                Conditions = conditions.Count > 0 ? conditions : null,
                Schedule = schedule,
                Destination = destination,
                ConfirmationPeriod = confirmationPeriod,
                Status = status,
                Template = args?.GetValueOrDefault("template")?.ToString(),
                IsDisabled = args?.GetValueOrDefault("isEnabled") is bool b ? !b : false,
                Icon = args?.GetValueOrDefault("icon")?.ToString(),
                ScheduleId = alertScheduleId,
                Initiator = InitiatorInfo.AsSystemInfo("MCP")
            };
        }

        private static List<PolicyConditionUpdate> BuildConditions(Dictionary<string, object> args)
        {
            var conditions = new List<PolicyConditionUpdate>();

            var conditionStr = args?.GetValueOrDefault("condition")?.ToString();
            var propertyStr = args?.GetValueOrDefault("property")?.ToString() ?? "Value";
            var targetValue = args?.GetValueOrDefault("targetValue")?.ToString();
            var combinationStr = args?.GetValueOrDefault("combination")?.ToString() ?? "and";

            if (string.IsNullOrEmpty(conditionStr) && string.IsNullOrEmpty(targetValue))
                return conditions;

            var operation = ParseOperation(conditionStr);
            var property = ParseProperty(propertyStr);
            var combination = combinationStr.ToLower() == "or" ? PolicyCombination.Or : PolicyCombination.And;
            var target = new TargetValue(TargetType.Const, targetValue ?? "0");

            conditions.Add(new PolicyConditionUpdate(operation, property, target, combination));

            return conditions;
        }

        private static PolicyScheduleUpdate BuildPolicySchedule(Dictionary<string, object> args)
        {
            var repeatModeStr = args?.GetValueOrDefault("repeatMode")?.ToString();
            var instantSend = args?.GetValueOrDefault("instantSend") is bool b && b;
            var timeStr = args?.GetValueOrDefault("scheduleTime")?.ToString();

            if (string.IsNullOrEmpty(repeatModeStr) && !instantSend && string.IsNullOrEmpty(timeStr))
                return null;

            var repeatMode = ParseRepeatMode(repeatModeStr);
            DateTime? time = null;
            if (!string.IsNullOrEmpty(timeStr))
                DateTime.TryParse(timeStr, out var parsedTime);

            return new PolicyScheduleUpdate
            {
                RepeatMode = repeatMode,
                InstantSend = instantSend,
                Time = time ?? DateTime.UtcNow
            };
        }

        private static PolicyDestinationUpdate BuildDestination(Dictionary<string, object> args)
        {
            var modeStr = args?.GetValueOrDefault("destinationMode")?.ToString();

            if (string.IsNullOrEmpty(modeStr))
                return null;

            var mode = ParseDestinationMode(modeStr);
            return new PolicyDestinationUpdate(mode);
        }

        private static HSMCommon.Model.SensorStatus BuildStatus(Dictionary<string, object> args)
        {
            var statusStr = args?.GetValueOrDefault("triggerStatus")?.ToString();
            
            if (string.IsNullOrEmpty(statusStr))
                return HSMCommon.Model.SensorStatus.Ok;

            return statusStr.ToLower() == "error" 
                ? HSMCommon.Model.SensorStatus.Error 
                : HSMCommon.Model.SensorStatus.Ok;
        }

        private static PolicyOperation ParseOperation(string op)
        {
            return op?.ToLower() switch
            {
                "<=" or "lte" or "lessthanorequal" => PolicyOperation.LessThanOrEqual,
                "<" or "lt" or "lessthan" => PolicyOperation.LessThan,
                ">" or "gt" or "greaterthan" => PolicyOperation.GreaterThan,
                ">=" or "gte" or "greaterthanorequal" => PolicyOperation.GreaterThanOrEqual,
                "==" or "=" or "eq" or "equals" => PolicyOperation.Equal,
                "!=" or "<>" or "ne" or "notequal" => PolicyOperation.NotEqual,
                "ischanged" => PolicyOperation.IsChanged,
                "iserror" => PolicyOperation.IsError,
                "isok" => PolicyOperation.IsOk,
                "ischangedtoerror" => PolicyOperation.IsChangedToError,
                "ischangedtook" => PolicyOperation.IsChangedToOk,
                "contains" => PolicyOperation.Contains,
                "startswith" => PolicyOperation.StartsWith,
                "endswith" => PolicyOperation.EndsWith,
                "receivednewvalue" => PolicyOperation.ReceivedNewValue,
                _ => PolicyOperation.GreaterThan
            };
        }

        private static PolicyProperty ParseProperty(string prop)
        {
            return prop?.ToLower() switch
            {
                "status" => PolicyProperty.Status,
                "comment" => PolicyProperty.Comment,
                "value" => PolicyProperty.Value,
                "min" => PolicyProperty.Min,
                "max" => PolicyProperty.Max,
                "mean" => PolicyProperty.Mean,
                "count" => PolicyProperty.Count,
                "lastvalue" => PolicyProperty.LastValue,
                "firstvalue" => PolicyProperty.FirstValue,
                "length" or "valuelength" => PolicyProperty.Length,
                "size" or "originalsize" => PolicyProperty.OriginalSize,
                "newdata" or "newsensordata" => PolicyProperty.NewSensorData,
                "emavalue" => PolicyProperty.EmaValue,
                "emamin" => PolicyProperty.EmaMin,
                "emamax" => PolicyProperty.EmaMax,
                "emean" or "emamean" => PolicyProperty.EmaMean,
                "emacount" => PolicyProperty.EmaCount,
                _ => PolicyProperty.Value
            };
        }

        private static AlertRepeatMode ParseRepeatMode(string mode)
        {
            return mode?.ToLower() switch
            {
                "immediately" or "0" => AlertRepeatMode.Immediately,
                "fiveminutes" or "5" or "5min" => AlertRepeatMode.FiveMinutes,
                "tenminutes" or "10" or "10min" => AlertRepeatMode.TenMinutes,
                "fifteenminutes" or "15" or "15min" => AlertRepeatMode.FifteenMinutes,
                "thirtyminutes" or "30" or "30min" => AlertRepeatMode.ThirtyMinutes,
                "hourly" or "1h" or "1hour" => AlertRepeatMode.Hourly,
                "daily" or "1d" or "1day" => AlertRepeatMode.Daily,
                "weekly" or "1w" or "1week" => AlertRepeatMode.Weekly,
                _ => AlertRepeatMode.Immediately
            };
        }

        private static PolicyDestinationMode ParseDestinationMode(string mode)
        {
            return mode?.ToLower() switch
            {
                "empty" or "0" => PolicyDestinationMode.Empty,
                "fromparent" or "default" or "1" => PolicyDestinationMode.FromParent,
                "allchats" or "2" => PolicyDestinationMode.AllChats,
                "custom" or "100" => PolicyDestinationMode.Custom,
                _ => PolicyDestinationMode.Empty
            };
        }

        private static Guid? ParseGuid(string value)
        {
            if (string.IsNullOrEmpty(value))
                return null;
            
            if (Guid.TryParse(value, out var guid))
                return guid;
            
            return null;
        }

        private static string GetAlertDisplayName(DataAlertViewModelBase alert)
        {
            var condition = alert.Conditions.FirstOrDefault();
            if (condition?.Target != null)
            {
                var opName = condition.Operation switch
                {
                    PolicyOperation.GreaterThan => ">",
                    PolicyOperation.LessThan => "<",
                    PolicyOperation.GreaterThanOrEqual => ">=",
                    PolicyOperation.LessThanOrEqual => "<=",
                    PolicyOperation.Equal => "=",
                    PolicyOperation.NotEqual => "!=",
                    PolicyOperation.IsChanged => "is changed",
                    PolicyOperation.IsError => "is error",
                    PolicyOperation.IsOk => "is ok",
                    PolicyOperation.Contains => "contains",
                    PolicyOperation.StartsWith => "starts with",
                    PolicyOperation.EndsWith => "ends with",
                    _ => condition.Operation.ToString()
                };
                return $"{condition.Property} {opName} {condition.Target}";
            }
            return "Alert";
        }
    }
}
