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
using Microsoft.Extensions.Logging;
using SensorStatus = HSMServer.Model.TreeViewModel.SensorStatus;

namespace HSMServer.Controllers.MCP
{
    public class McpToolService : IMcpToolService
    {
        private readonly ITreeValuesCache _cache;
        private readonly TreeViewModel _tree;
        private readonly IAlertScheduleProvider _scheduleProvider;
        private readonly IUserManager _userManager;
        private readonly ILogger<McpToolService> _logger;

        public McpToolService(ITreeValuesCache cache, TreeViewModel tree, IAlertScheduleProvider scheduleProvider,
            IUserManager userManager, ILogger<McpToolService> logger)
        {
            _cache = cache;
            _tree = tree;
            _scheduleProvider = scheduleProvider;
            _userManager = userManager;
            _logger = logger;
        }


        public List<ToolDefinition> GetToolDefinitions()
        {
            return new List<ToolDefinition>
            {
                new("list_sensors", "List all sensors with optional filtering", new { productId = "", status = "" }),
                new("get_sensor", "Get sensor details by ID", new { sensorId = "" }, new[] { "sensorId" }),
                new("get_sensor_history", "Get sensor value history", new { sensorId = "", from = "", to = "" }, new[] { "sensorId" }),
                new("list_products", "List all products/folders", null),
                new("list_schedules", "List all alert schedules", null),
                new("get_schedule", "Get schedule details", new { scheduleId = "" }, new[] { "scheduleId" }),
                new("create_schedule", "Create a new alert schedule", new { name = "", timezone = "", schedule = "" }, new[] { "name", "schedule" }),
                new("update_schedule", "Update an existing alert schedule", new { scheduleId = "", name = "", timezone = "", schedule = "" }, new[] { "scheduleId" }),
                new("delete_schedule", "Delete an alert schedule", new { scheduleId = "" }, new[] { "scheduleId" }),
                new("list_alerts", "List all alerts for a sensor", new { sensorId = "" }),
                new("get_alert", "Get alert details", new { alertId = "" }, new[] { "alertId" }),
                new("create_alert", "Create a new alert for a sensor", new { sensorId = "", condition = "", property = "", targetValue = "", combination = "", isEnabled = false, template = "", icon = "", triggerStatus = "", confirmationPeriod = "", repeatMode = "", instantSend = false, scheduleTime = "", destinationMode = "", alertScheduleId = "" }, new[] { "sensorId" }),
                new("update_alert", "Update an existing alert", new { alertId = "", condition = "", property = "", targetValue = "", combination = "", isEnabled = false, template = "", icon = "", triggerStatus = "", confirmationPeriod = "", repeatMode = "", instantSend = false, scheduleTime = "", destinationMode = "", alertScheduleId = "" }, new[] { "alertId" }),
                new("delete_alert", "Delete an alert", new { alertId = "" }, new[] { "alertId" })
            };
        }

        public object ExecuteTool(string toolName, User user, Dictionary<string, object> args)
        {
            if (user == null)
                return new { error = "Authentication required" };

            return toolName switch
            {
                "list_sensors" => ListSensors(user, args),
                "get_sensor" => GetSensor(user, args),
                "list_products" => ListProducts(user),
                "list_schedules" => ListSchedules(),
                "get_schedule" => GetSchedule(args),
                "create_schedule" => CreateSchedule(user, args),
                "update_schedule" => UpdateSchedule(user, args),
                "delete_schedule" => DeleteSchedule(user, args),
                "list_alerts" => ListAlerts(user, args),
                "get_alert" => GetAlert(user, args),
                _ => new { error = $"Unknown tool: {toolName}" }
            };
        }

        public async Task<object> ExecuteToolAsync(string toolName, User user, Dictionary<string, object> args)
        {
            if (user == null)
                return new { error = "Authentication required" };

            return toolName switch
            {
                "get_sensor_history" => await GetSensorHistory(user, args),
                "create_alert" => await CreateAlert(user, args),
                "update_alert" => await UpdateAlert(user, args),
                "delete_alert" => await DeleteAlert(user, args),
                _ => ExecuteTool(toolName, user, args)
            };
        }


        public object ListSensors(User user, Dictionary<string, object> args)
        {
            var status = args?.GetValueOrDefault("status")?.ToString();

            var sensors = _tree.Sensors.Values
                .Where(s => HasAccessToProduct(user, s.RootProduct?.Id ?? Guid.Empty))
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

        public object GetSensor(User user, Dictionary<string, object> args)
        {
            var sensorId = args?["sensorId"]?.ToString();
            if (string.IsNullOrEmpty(sensorId))
                return new { error = "sensorId is required" };

            if (!Guid.TryParse(sensorId, out var guid))
                return new { error = "Invalid sensorId format" };

            if (!_tree.Sensors.TryGetValue(guid, out var sensor))
                return new { error = "Sensor not found" };

            if (!HasAccessToProduct(user, sensor.RootProduct?.Id ?? Guid.Empty))
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

        public async Task<object> GetSensorHistory(User user, Dictionary<string, object> args)
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

            if (!HasAccessToProduct(user, sensor.RootProduct?.Id ?? Guid.Empty))
                return new { error = "Access denied: sensor belongs to different product" };

            var from = string.IsNullOrEmpty(fromStr) ? DateTime.UtcNow.AddDays(-1) :
                DateTime.TryParse(fromStr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedFrom) ? parsedFrom : DateTime.UtcNow.AddDays(-1);
            var to = string.IsNullOrEmpty(toStr) ? DateTime.UtcNow :
                DateTime.TryParse(toStr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedTo) ? parsedTo : DateTime.UtcNow;

            var values = new List<object>();
            const int maxResults = 10000;
            await foreach (var batch in _cache.GetSensorValuesPage(sensorGuid, from, to, 1000))
            {
                foreach (var v in batch)
                {
                    if (values.Count >= maxResults)
                    {
                        values.Add(new { warning = $"Results truncated at {maxResults} entries. Narrow the time range." });
                        return values;
                    }

                    values.Add(new
                    {
                        time = v.Time,
                        status = v.Status.ToString(),
                        value = v.RawValue?.ToString()
                    });
                }
            }

            return values;
        }

        public object ListProducts(User user)
        {
            var products = _tree.Nodes.Values.Where(p => HasAccessToProduct(user, p.Id));

            return products.Select(p => new
            {
                id = p.Id,
                name = p.Name,
                path = p.Path,
                sensorCount = p.AllSensorsCount
            }).ToList();
        }

        public object ListSchedules()
        {
            return _scheduleProvider.GetAllSchedules().Select(s => new
            {
                id = s.Id,
                name = s.Name,
                timezone = s.Timezone,
                schedule = s.Schedule
            }).ToList();
        }

        public object GetSchedule(Dictionary<string, object> args)
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

        public object CreateSchedule(User user, Dictionary<string, object> args)
        {
            if (user == null || !user.IsAdmin)
                return new { error = "Access denied: admin rights required to manage schedules" };

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

            _logger.LogInformation("MCP: Schedule {ScheduleId} created by user {User}", newSchedule.Id, user.Name);

            return new
            {
                id = newSchedule.Id,
                name = newSchedule.Name,
                timezone = newSchedule.Timezone,
                schedule = newSchedule.Schedule,
                message = "Schedule created successfully"
            };
        }

        public object UpdateSchedule(User user, Dictionary<string, object> args)
        {
            if (user == null || !user.IsAdmin)
                return new { error = "Access denied: admin rights required to manage schedules" };

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

            var updatedSchedule = new AlertSchedule
            {
                Id = existingSchedule.Id,
                Name = !string.IsNullOrEmpty(name) ? name : existingSchedule.Name,
                Timezone = !string.IsNullOrEmpty(timezone) ? timezone : existingSchedule.Timezone,
                Schedule = !string.IsNullOrEmpty(schedule) ? schedule : existingSchedule.Schedule,
            };

            _scheduleProvider.SaveSchedule(updatedSchedule);

            _logger.LogInformation("MCP: Schedule {ScheduleId} updated by user {User}", scheduleGuid, user.Name);

            return new
            {
                id = updatedSchedule.Id,
                name = updatedSchedule.Name,
                timezone = updatedSchedule.Timezone,
                schedule = updatedSchedule.Schedule,
                message = "Schedule updated successfully"
            };
        }

        public object DeleteSchedule(User user, Dictionary<string, object> args)
        {
            if (user == null || !user.IsAdmin)
                return new { error = "Access denied: admin rights required to manage schedules" };

            var scheduleId = args?["scheduleId"]?.ToString();

            if (string.IsNullOrEmpty(scheduleId))
                return new { error = "scheduleId is required" };

            if (!Guid.TryParse(scheduleId, out var scheduleGuid))
                return new { error = "Invalid scheduleId format" };

            var existingSchedule = _scheduleProvider.GetSchedule(scheduleGuid);
            if (existingSchedule == null)
                return new { error = "Schedule not found" };

            _scheduleProvider.DeleteSchedule(scheduleGuid);

            _logger.LogInformation("MCP: Schedule {ScheduleId} deleted by user {User}", scheduleGuid, user.Name);

            return new { message = "Schedule deleted successfully" };
        }

        public object ListAlerts(User user, Dictionary<string, object> args)
        {
            var sensorId = args?.GetValueOrDefault("sensorId")?.ToString();

            var alerts = new List<object>();

            var sensors = _tree.Sensors.Values.Where(s => HasAccessToProduct(user, s.RootProduct?.Id ?? Guid.Empty));

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

        public object GetAlert(User user, Dictionary<string, object> args)
        {
            var alertId = args?["alertId"]?.ToString();
            if (string.IsNullOrEmpty(alertId))
                return new { error = "alertId is required" };

            if (!Guid.TryParse(alertId, out var alertGuid))
                return new { error = "Invalid alertId format" };

            var sensors = _tree.Sensors.Values.Where(s => HasAccessToProduct(user, s.RootProduct?.Id ?? Guid.Empty));

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

        public async Task<object> CreateAlert(User user, Dictionary<string, object> args)
        {
            var sensorId = args?["sensorId"]?.ToString();
            if (string.IsNullOrEmpty(sensorId))
                return new { error = "sensorId is required" };

            if (!Guid.TryParse(sensorId, out var sensorGuid))
                return new { error = "Invalid sensorId format" };

            if (!_tree.Sensors.TryGetValue(sensorGuid, out var sensor))
                return new { error = "Sensor not found" };

            if (!HasAccessToProduct(user, sensor.RootProduct?.Id ?? Guid.Empty))
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

        public async Task<object> UpdateAlert(User user, Dictionary<string, object> args)
        {
            var alertId = args?["alertId"]?.ToString();
            if (string.IsNullOrEmpty(alertId))
                return new { error = "alertId is required" };

            if (!Guid.TryParse(alertId, out var alertGuid))
                return new { error = "Invalid alertId format" };

            var sensors = _tree.Sensors.Values.Where(s => HasAccessToProduct(user, s.RootProduct?.Id ?? Guid.Empty));

            foreach (var sensor in sensors)
            {
                if (sensor.DataAlerts != null)
                {
                    foreach (var alertList in sensor.DataAlerts.Values)
                    {
                        var alert = alertList.FirstOrDefault(a => a.Id == alertGuid);
                        if (alert != null)
                        {
                            // Preserve existing conditions unless new ones are provided
                            var newConditions = BuildConditions(args);
                            var conditions = newConditions.Count > 0 ? newConditions : ConvertExistingConditions(alert);

                            var alertScheduleId = args?.ContainsKey("alertScheduleId") == true
                                ? ParseGuid(args?.GetValueOrDefault("alertScheduleId")?.ToString())
                                : alert.ScheduleId;

                            var isDisabled = args?.ContainsKey("isEnabled") == true
                                ? !(bool)args["isEnabled"]
                                : alert.IsDisabled;

                            var policyUpdate = new PolicyUpdate
                            {
                                Id = alertGuid,
                                Conditions = conditions,
                                Schedule = BuildPolicySchedule(args),
                                Destination = BuildDestination(args),
                                Status = BuildStatus(args),
                                Template = args?.GetValueOrDefault("template")?.ToString(),
                                IsDisabled = isDisabled,
                                Icon = args?.GetValueOrDefault("icon")?.ToString(),
                                ScheduleId = alertScheduleId,
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
                                message = result.IsOk ? "Alert updated successfully" : $"Error: {result.Error}"
                            };
                        }
                    }
                }
            }

            return new { error = "Alert not found" };
        }

        public async Task<object> DeleteAlert(User user, Dictionary<string, object> args)
        {
            var alertId = args?["alertId"]?.ToString();

            if (string.IsNullOrEmpty(alertId))
                return new { error = "alertId is required" };

            if (!Guid.TryParse(alertId, out var alertGuid))
                return new { error = "Invalid alertId format" };

            var sensors = _tree.Sensors.Values.Where(s => HasAccessToProduct(user, s.RootProduct?.Id ?? Guid.Empty));

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
                                message = result.IsOk ? "Alert disabled successfully" : $"Error: {result.Error}"
                            };
                        }
                    }
                }
            }

            return new { error = "Alert not found" };
        }


        private static List<PolicyConditionUpdate> ConvertExistingConditions(DataAlertViewModelBase alert)
        {
            return alert.Conditions
                .Where(c => c.Property != AlertProperty.ConfirmationPeriod && c.Property != AlertProperty.TimeToLive)
                .Select(c => new PolicyConditionUpdate(
                    c.Operation ?? PolicyOperation.GreaterThan,
                    ParseProperty(c.Property.ToString()),
                    new TargetValue(TargetType.Const, c.Target ?? "0"),
                    PolicyCombination.And))
                .ToList();
        }


        private static bool HasAccessToProduct(User user, Guid productId)
        {
            if (user == null)
                return false;

            return user.IsProductAvailable(productId) || user.IsAdmin;
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
            if (!string.IsNullOrEmpty(timeStr) && DateTime.TryParse(timeStr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedTime))
                time = parsedTime;

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
