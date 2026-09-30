using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using HSMCommon.Model;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.TableOfChanges;
using SensorStatus = HSMCommon.Model.SensorStatus;

namespace HSMServer.Model.ManagementApi.Alerts
{
    // Mapping between the durable policy models and the alert-administration
    // wire DTOs (#1500). Read side is TOTAL — a stored policy outside the API's
    // expression range (legacy rows, template-minted oddities) degrades to the
    // closest DTO instead of throwing: a read must never 500 over data. Write
    // side is only reached AFTER validation, so it maps without defensive
    // branches beyond the parse fallbacks.
    internal static class AlertPolicyDtoMapper
    {
        // Read: a regular data policy.
        public static PolicyDto ToDto(Policy policy, SensorType sensorType) => new()
        {
            Id = policy.Id,
            Conditions = [.. policy.Conditions.Select(c => ToDto(c, sensorType))],
            Destination = ToDto(policy.Destination),
            Notification = ToDto(policy.Template, policy.Schedule),
            Icon = string.IsNullOrEmpty(policy.Icon) ? null : policy.Icon,
            Status = policy.Status == SensorStatus.Error ? "Error" : null,
            ConfirmationPeriod = policy.ConfirmationPeriod is { } ticks
                ? System.TimeSpan.FromTicks(ticks).ToString("c", CultureInfo.InvariantCulture)
                : null,
            IsDisabled = policy.IsDisabled,
            ScheduleId = policy.ScheduleId,
            TemplateId = policy.TemplateId,
            TemplateAlertId = policy.TemplateAlertId,
        };

        // Read: a TTL policy.
        public static TtlPolicyDto ToDto(TTLPolicy policy) => new()
        {
            Id = policy.Id,
            // An explicit interval round-trips as a TimeSpan string; a
            // from-parent policy carries inherit=true; a policy resolving to no
            // interval anywhere (None) reads interval=null, inherit=false.
            Interval = !policy.IsTTLFromParent && policy.TTLInterval is { IsNone: false } interval
                ? System.TimeSpan.FromTicks(interval.Ticks).ToString("c", CultureInfo.InvariantCulture)
                : null,
            Inherit = policy.IsTTLFromParent,
            Destination = ToDto(policy.Destination),
            Notification = ToDto(policy.Template, policy.Schedule),
            Icon = string.IsNullOrEmpty(policy.Icon) ? null : policy.Icon,
            Status = policy.Status == SensorStatus.Error ? "Error" : null,
            IsDisabled = policy.IsDisabled,
            ScheduleId = policy.ScheduleId,
            TemplateId = policy.TemplateId,
            TemplateAlertId = policy.TemplateAlertId,
        };

        public static ProductPolicyDto ToProductDto(Policy policy, BaseSensorModel sensor) => new()
        {
            SensorId = sensor.Id,
            SensorPath = sensor.FullPath,
            Policy = ToDto(policy, sensor.Type),
        };

        public static ProductPolicyDto ToProductDto(PolicyDto policy, BaseSensorModel sensor) => new()
        {
            SensorId = sensor.Id,
            SensorPath = sensor.FullPath,
            Policy = policy,
        };

        private static AlertConditionDto ToDto(PolicyCondition condition, SensorType sensorType)
        {
            // A stored condition the current editor surface does not offer
            // (template-minted or legacy) still reads back: property and
            // operation by name, target as its raw stored string.
            object target = null;

            if (condition.Target is { Type: TargetType.Const } targetValue &&
                AlertPolicyConditionRules.TryGetRule(sensorType, condition.Property, out var rule) &&
                TryParseStoredTarget(targetValue.Value, rule.TargetKind, out var typed))
            {
                target = typed;
            }
            else if (condition.Target is { Type: TargetType.Const } raw)
            {
                target = raw.Value;
            }

            return new AlertConditionDto
            {
                Property = condition.Property.ToString(),
                Operation = condition.Operation.ToString(),
                Target = target,
                Combination = condition.Combination.ToString(),
            };
        }

        // The read-side twin of the validator's serialization: the stored
        // invariant string parsed back into the typed JSON value the write
        // surface expects, so a GET response can be POSTed back unchanged.
        private static bool TryParseStoredTarget(string stored, ConditionTargetKind kind, out object typed)
        {
            typed = null;

            switch (kind)
            {
                case ConditionTargetKind.Integer:
                case ConditionTargetKind.Long:
                    if (long.TryParse(stored, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
                    {
                        typed = integer;
                        return true;
                    }

                    return false;

                case ConditionTargetKind.Double:
                    if (double.TryParse(stored, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                    {
                        typed = number;
                        return true;
                    }

                    return false;

                case ConditionTargetKind.TimeSpan:
                    if (System.TimeSpan.TryParse(stored, CultureInfo.InvariantCulture, out var interval))
                    {
                        typed = interval.ToString("c", CultureInfo.InvariantCulture);
                        return true;
                    }

                    return false;

                case ConditionTargetKind.Version:
                case ConditionTargetKind.Text:
                    typed = stored;
                    return stored is not null;

                default:
                    return false;
            }
        }

        private static AlertDestinationDto ToDto(PolicyDestination destination) => new()
        {
            Mode = destination.Mode.ToString(),
            Chats = [.. destination.Chats.Keys.OrderBy(id => id)],
        };

        private static AlertNotificationDto ToDto(string template, PolicySchedule schedule) =>
            string.IsNullOrEmpty(template)
                ? null
                : new AlertNotificationDto
                {
                    Template = template,
                    Repeat = (schedule?.RepeatMode ?? AlertRepeatMode.Immediately).ToString(),
                    InstantSend = schedule?.InstantSend ?? false,
                    // MinValue is the core's "unset" — published as null.
                    StartAt = schedule?.Time is { } time && time != DateTime.MinValue
                        ? DateTime.SpecifyKind(time, DateTimeKind.Utc)
                        : null,
                };

        // Write: build the core's full policy update from a validated DTO. The
        // id is the caller's decision (fresh on create, the route id on patch);
        // availableChats is the node's chat map the validator already checked
        // every referenced chat against (names are the manager's current ones,
        // the same normalization the web editor performs).
        //
        // Template linkage is SERVER-OWNED (#1501 round-1): templateId/
        // templateAlertId never come from the request body — create passes
        // nothing (a fresh policy is never template-owned; the core's add gate
        // would silently skip it, surfacing as a misleading 409), update passes
        // the STORED policy's values (a PATCH cannot mint or un-mint template
        // ownership).
        public static PolicyUpdate ToUpdate(PolicyDto dto, Guid policyId, InitiatorInfo initiator,
            List<PolicyConditionUpdate> conditions, Dictionary<Guid, string> availableChats,
            Guid? templateId = null, Guid? templateAlertId = null) => new()
        {
            Id = policyId,
            Conditions = conditions,
            Destination = ToUpdate(dto.Destination, dto.Notification is not null, availableChats),
            Schedule = ToUpdate(dto.Notification),
            Template = dto.Notification?.Template,
            Icon = string.IsNullOrEmpty(dto.Icon) ? null : dto.Icon,
            Status = dto.Status == "Error" ? SensorStatus.Error : SensorStatus.Ok,
            ConfirmationPeriod = ParseIntervalTicks(dto.ConfirmationPeriod),
            IsDisabled = dto.IsDisabled,
            ScheduleId = dto.ScheduleId,
            TemplateId = templateId,
            TemplateAlertId = templateAlertId,
            Initiator = initiator,
        };

        public static PolicyUpdate ToUpdate(TtlPolicyDto dto, Guid policyId, InitiatorInfo initiator, long? ttl,
            Dictionary<Guid, string> availableChats, Guid? templateId = null, Guid? templateAlertId = null) => new()
        {
            Id = policyId,
            Destination = ToUpdate(dto.Destination, dto.Notification is not null, availableChats),
            Schedule = ToUpdate(dto.Notification),
            Template = dto.Notification?.Template,
            Icon = string.IsNullOrEmpty(dto.Icon) ? null : dto.Icon,
            Status = dto.Status == "Error" ? SensorStatus.Error : SensorStatus.Ok,
            IsDisabled = dto.IsDisabled,
            ScheduleId = dto.ScheduleId,
            TemplateId = templateId,
            TemplateAlertId = templateAlertId,
            Initiator = initiator,
            // The server-owned translation of the inherit switch (#1500): null
            // is the core's EXPLICIT reset-to-parent in full-list semantics —
            // a value the client may only express through inherit=true.
            TTL = ttl,
        };

        private static PolicyDestinationUpdate ToUpdate(AlertDestinationDto destination, bool hasNotification,
            Dictionary<Guid, string> availableChats)
        {
            // Without a notification there is nothing to route: the destination
            // degrades to Empty deterministically instead of keeping stale chats.
            if (!hasNotification || destination is null)
                return new PolicyDestinationUpdate(PolicyDestinationMode.Empty);

            // Every documented mode passes through, NotInitialized included (an
            // echo of an unconfigured destination — see the DTO docs).
            var mode = Enum.TryParse<PolicyDestinationMode>(destination.Mode, ignoreCase: true, out var parsed) &&
                       Enum.IsDefined(parsed)
                ? parsed
                : PolicyDestinationMode.Empty;

            var chats = new Dictionary<Guid, string>();

            // An explicit wire null means "no chats", exactly like an empty
            // array — System.Text.Json materializes "chats": null over the
            // DTO's [] default, so the default does not protect this loop
            // (#1501 round-3, F2).
            foreach (var chatId in destination.Chats ?? [])
                chats[chatId] = availableChats.TryGetValue(chatId, out var name) ? name : chatId.ToString();

            return new PolicyDestinationUpdate(chats, mode);
        }

        private static PolicyScheduleUpdate ToUpdate(AlertNotificationDto notification)
        {
            if (notification is null)
                return new PolicyScheduleUpdate();

            var repeat = Enum.TryParse<AlertRepeatMode>(notification.Repeat, ignoreCase: true, out var parsed) &&
                         Enum.IsDefined(parsed)
                ? parsed
                : AlertRepeatMode.Immediately;

            return new PolicyScheduleUpdate
            {
                RepeatMode = repeat,
                InstantSend = notification.InstantSend,
                Time = notification.StartAt is { } start ? DateTime.SpecifyKind(start, DateTimeKind.Utc) : DateTime.MinValue,
            };
        }

        internal static long? ParseIntervalTicks(string interval) =>
            string.IsNullOrEmpty(interval) ||
            !System.TimeSpan.TryParse(interval, CultureInfo.InvariantCulture, out var parsed)
                ? null
                : parsed.Ticks;

        // The canonical string form of a condition target, whatever side it came
        // from: a request-side System.Text.Json element or a read-side parsed
        // CLR value. Equality of the canonical forms is how the template-guard
        // decides a request is a pure disable toggle.
        internal static string ToCanonicalTargetString(object target) => target switch
        {
            null => null,
            System.Text.Json.JsonElement { ValueKind: JsonValueKind.Number } number =>
                number.TryGetInt64(out var integer)
                    ? integer.ToString(CultureInfo.InvariantCulture)
                    : number.GetDouble().ToString("R", CultureInfo.InvariantCulture),
            System.Text.Json.JsonElement { ValueKind: JsonValueKind.String } text => text.GetString(),
            System.Text.Json.JsonElement => null,
            long l => l.ToString(CultureInfo.InvariantCulture),
            double d => d.ToString("R", CultureInfo.InvariantCulture),
            _ => target.ToString(),
        };
    }
}
