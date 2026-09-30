using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using HSMCommon.Model;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model.Policies;

namespace HSMServer.Model.ManagementApi.Alerts
{
    // The per-sensor-type condition surface of the alert-administration API
    // (#1500): which condition properties each sensor type offers, which
    // operations each property accepts, and what a condition's target looks
    // like. The table MIRRORS the web editor's condition view models
    // (Model/DataAlerts/ConditionViewModels — NumericConditionViewModel,
    // StringConditionViewModel, BarConditionViewModel, ...): the same surface an
    // operator sees in the dropdowns, expressed once for machines. A condition
    // outside the table is a 422, never a silently-degrading policy.
    internal enum ConditionTargetKind
    {
        // The operation compares without a constant (IsChanged, IsOk, ...).
        None,

        // JSON number, integer range (the core parses int).
        Integer,

        // JSON number, long range (File OriginalSize).
        Long,

        // JSON number, any finite double.
        Double,

        // JSON string.
        Text,

        // JSON string parseable as TimeSpan ("d.hh:mm:ss" / "hh:mm:ss").
        TimeSpan,

        // JSON string parseable as System.Version ("1.2.3").
        Version,
    }

    internal sealed record ConditionRule(PolicyProperty Property, ConditionTargetKind TargetKind, params PolicyOperation[] Operations);

    internal static class AlertPolicyConditionRules
    {
        // The operation sets, exactly as the editor's operation pickers offer them.
        private static readonly PolicyOperation[] NumericOperations =
        [
            PolicyOperation.LessThanOrEqual, PolicyOperation.LessThan, PolicyOperation.GreaterThan,
            PolicyOperation.GreaterThanOrEqual, PolicyOperation.NotEqual, PolicyOperation.Equal,
        ];

        private static readonly PolicyOperation[] StatusOperations =
        [
            PolicyOperation.IsChanged, PolicyOperation.IsChangedToOk, PolicyOperation.IsChangedToError,
            PolicyOperation.IsOk, PolicyOperation.IsError,
        ];

        private static readonly PolicyOperation[] CommentOperations =
        [
            PolicyOperation.Equal, PolicyOperation.NotEqual, PolicyOperation.Contains,
            PolicyOperation.StartsWith, PolicyOperation.EndsWith, PolicyOperation.IsChanged,
        ];

        private static readonly PolicyOperation[] TextValueOperations =
        [
            PolicyOperation.Equal, PolicyOperation.NotEqual, PolicyOperation.Contains,
            PolicyOperation.StartsWith, PolicyOperation.EndsWith,
        ];

        // The common subset every type shares (CommonConditionViewModel).
        private static readonly ConditionRule[] CommonRules =
        [
            new(PolicyProperty.Status, ConditionTargetKind.None, StatusOperations),
            new(PolicyProperty.Comment, ConditionTargetKind.Text, CommentOperations),
            new(PolicyProperty.NewSensorData, ConditionTargetKind.None, PolicyOperation.ReceivedNewValue),
        ];

        private static readonly ConditionRule[] IntegerRules =
        [
            new(PolicyProperty.Value, ConditionTargetKind.Integer, NumericOperations),
            new(PolicyProperty.EmaValue, ConditionTargetKind.Double, NumericOperations),
        ];

        private static readonly ConditionRule[] DoubleRules =
        [
            new(PolicyProperty.Value, ConditionTargetKind.Double, NumericOperations),
            new(PolicyProperty.EmaValue, ConditionTargetKind.Double, NumericOperations),
        ];

        // Rate and Enum ride the same NumericConditionViewModel the numeric
        // sensors use; Enum's value is an integer code.
        private static readonly ConditionRule[] RateRules = DoubleRules;
        private static readonly ConditionRule[] EnumRules = IntegerRules;

        private static readonly ConditionRule[] TimeSpanRules =
        [
            new(PolicyProperty.Value, ConditionTargetKind.TimeSpan, NumericOperations),
        ];

        private static readonly ConditionRule[] VersionRules =
        [
            new(PolicyProperty.Value, ConditionTargetKind.Version, TextValueOperations),
        ];

        private static readonly ConditionRule[] StringRules =
        [
            new(PolicyProperty.Value, ConditionTargetKind.Text, TextValueOperations),
            new(PolicyProperty.Length, ConditionTargetKind.Integer, NumericOperations),
        ];

        private static readonly ConditionRule[] FileRules =
        [
            new(PolicyProperty.OriginalSize, ConditionTargetKind.Long, NumericOperations),
        ];

        // Bar statistics are int for IntegerBar and double for DoubleBar; Count
        // is int on both (the core's PolicyIntegerCondition); Ema* are double.
        private static readonly ConditionRule[] IntegerBarRules =
        [
            new(PolicyProperty.Min, ConditionTargetKind.Integer, NumericOperations),
            new(PolicyProperty.Max, ConditionTargetKind.Integer, NumericOperations),
            new(PolicyProperty.Mean, ConditionTargetKind.Integer, NumericOperations),
            new(PolicyProperty.Count, ConditionTargetKind.Integer, NumericOperations),
            new(PolicyProperty.FirstValue, ConditionTargetKind.Integer, NumericOperations),
            new(PolicyProperty.LastValue, ConditionTargetKind.Integer, NumericOperations),
            new(PolicyProperty.EmaMin, ConditionTargetKind.Double, NumericOperations),
            new(PolicyProperty.EmaMax, ConditionTargetKind.Double, NumericOperations),
            new(PolicyProperty.EmaMean, ConditionTargetKind.Double, NumericOperations),
            new(PolicyProperty.EmaCount, ConditionTargetKind.Double, NumericOperations),
        ];

        private static readonly ConditionRule[] DoubleBarRules =
        [
            new(PolicyProperty.Min, ConditionTargetKind.Double, NumericOperations),
            new(PolicyProperty.Max, ConditionTargetKind.Double, NumericOperations),
            new(PolicyProperty.Mean, ConditionTargetKind.Double, NumericOperations),
            new(PolicyProperty.Count, ConditionTargetKind.Integer, NumericOperations),
            new(PolicyProperty.FirstValue, ConditionTargetKind.Double, NumericOperations),
            new(PolicyProperty.LastValue, ConditionTargetKind.Double, NumericOperations),
            new(PolicyProperty.EmaMin, ConditionTargetKind.Double, NumericOperations),
            new(PolicyProperty.EmaMax, ConditionTargetKind.Double, NumericOperations),
            new(PolicyProperty.EmaMean, ConditionTargetKind.Double, NumericOperations),
            new(PolicyProperty.EmaCount, ConditionTargetKind.Double, NumericOperations),
        ];

        // Boolean has no Value condition in the editor surface (the
        // CommonConditionViewModel subset only) — mirrored here.
        private static readonly ConditionRule[] BooleanRules = [];


        // The rule lookup of the validation pass; unknown (type, property) pairs
        // answer false and the caller reports the 422 with the supported list.
        public static bool TryGetRule(SensorType sensorType, PolicyProperty property, out ConditionRule rule)
        {
            rule = Table(sensorType).FirstOrDefault(r => r.Property == property);

            if (rule is not null)
                return true;

            // The common subset is always available, whatever the type.
            rule = CommonRules.FirstOrDefault(r => r.Property == property);

            return rule is not null;
        }

        public static string DescribeSupportedProperties(SensorType sensorType) =>
            string.Join(", ", Table(sensorType).Concat(CommonRules).Select(r => r.Property.ToString()));

        // The property names an agent may send for this sensor type — the doc
        // error message quotes it, so a 422 is self-correcting.
        private static IEnumerable<ConditionRule> Table(SensorType sensorType) => sensorType switch
        {
            SensorType.Integer => IntegerRules,
            SensorType.Double => DoubleRules,
            SensorType.Rate => RateRules,
            SensorType.Enum => EnumRules,
            SensorType.TimeSpan => TimeSpanRules,
            SensorType.Version => VersionRules,
            SensorType.String => StringRules,
            SensorType.File => FileRules,
            SensorType.IntegerBar => IntegerBarRules,
            SensorType.DoubleBar => DoubleBarRules,
            SensorType.Boolean => BooleanRules,
            _ => [],
        };
    }

    // Validation of one condition DTO against the rule table, producing the
    // core's PolicyConditionUpdate or a field-keyed error. Pure — no cache and
    // no clock; the service orchestrates around it.
    internal static class AlertPolicyConditionValidator
    {
        public const int MaxConditions = 10;

        public static bool TryBuildConditions(SensorType sensorType, Guid sensorId, List<AlertConditionDto> conditions,
            out List<PolicyConditionUpdate> built, out Dictionary<string, string[]> errors)
        {
            built = [];
            var errorMap = new Dictionary<string, List<string>>();

            if (conditions is null || conditions.Count == 0)
            {
                Add(errorMap, "conditions", "At least one condition is required.");
                errors = ToErrors(errorMap);
                return false;
            }

            if (conditions.Count > MaxConditions)
            {
                Add(errorMap, "conditions", $"At most {MaxConditions} conditions are allowed.");
                errors = ToErrors(errorMap);
                return false;
            }

            for (var index = 0; index < conditions.Count; index++)
            {
                var condition = conditions[index];
                var prefix = $"conditions[{index}]";

                if (condition is null)
                {
                    Add(errorMap, prefix, "A condition entry must not be null.");
                    continue;
                }

                // Enum names are parsed case-insensitively; the canonical casing
                // is what the read surface and the spec publish.
                if (string.IsNullOrEmpty(condition.Property) ||
                    !Enum.TryParse<PolicyProperty>(condition.Property, ignoreCase: true, out var property) ||
                    !Enum.IsDefined(property))
                {
                    Add(errorMap, $"{prefix}.property",
                        $"Unknown condition property '{condition.Property}'. Supported for {sensorType}: {AlertPolicyConditionRules.DescribeSupportedProperties(sensorType)}.");
                    continue;
                }

                if (string.IsNullOrEmpty(condition.Operation) ||
                    !Enum.TryParse<PolicyOperation>(condition.Operation, ignoreCase: true, out var operation) ||
                    !Enum.IsDefined(operation))
                {
                    Add(errorMap, $"{prefix}.operation", $"Unknown condition operation '{condition.Operation}'.");
                    continue;
                }

                if (!AlertPolicyConditionRules.TryGetRule(sensorType, property, out var rule))
                {
                    Add(errorMap, $"{prefix}.property",
                        $"Property '{property}' is not supported by {sensorType} sensors. Supported: {AlertPolicyConditionRules.DescribeSupportedProperties(sensorType)}.");
                    continue;
                }

                if (!rule.Operations.Contains(operation))
                {
                    Add(errorMap, $"{prefix}.operation",
                        $"Operation '{operation}' is not supported for property '{property}'. Supported: {string.Join(", ", rule.Operations)}.");
                    continue;
                }

                PolicyCombination combination = PolicyCombination.And;

                if (!string.IsNullOrEmpty(condition.Combination) &&
                    (!Enum.TryParse<PolicyCombination>(condition.Combination, ignoreCase: true, out combination) ||
                     !Enum.IsDefined(combination)))
                {
                    Add(errorMap, $"{prefix}.combination", $"Unknown condition combination '{condition.Combination}' (And or Or).");
                    continue;
                }

                if (!TryBuildTarget(rule, operation, condition.Target, sensorId, out var target, out var targetError))
                {
                    Add(errorMap, $"{prefix}.target", targetError);
                    continue;
                }

                built.Add(new PolicyConditionUpdate(operation, property, target, combination));
            }

            errors = ToErrors(errorMap);

            return errors.Count == 0;
        }

        // Targetless operations get the core's LastValue(self) target — the same
        // value the web editor's form submits for them; Const targets are
        // validated against the property's target kind and serialized in the
        // invariant form the core's parsers read back.
        private static bool TryBuildTarget(ConditionRule rule, PolicyOperation operation, object rawTarget,
            Guid sensorId, out TargetValue target, out string error)
        {
            target = null;
            error = null;

            var element = rawTarget as JsonElement?;

            if (rule.TargetKind is ConditionTargetKind.None)
            {
                if (element is { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined })
                {
                    error = $"Operation '{operation}' takes no target.";
                    return false;
                }

                target = new TargetValue(TargetType.LastValue, sensorId.ToString());
                return true;
            }

            if (element is not { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined })
            {
                error = $"Operation '{operation}' requires a target.";
                return false;
            }

            var value = element.Value;

            switch (rule.TargetKind)
            {
                case ConditionTargetKind.Integer:
                    if (value.ValueKind is not JsonValueKind.Number || !value.TryGetInt64(out var integer) ||
                        integer is < int.MinValue or > int.MaxValue)
                    {
                        error = "The target must be an integer number.";
                        return false;
                    }

                    target = new TargetValue(TargetType.Const, integer.ToString(CultureInfo.InvariantCulture));
                    return true;

                case ConditionTargetKind.Long:
                    if (value.ValueKind is not JsonValueKind.Number || !value.TryGetInt64(out var longValue))
                    {
                        error = "The target must be an integer number.";
                        return false;
                    }

                    target = new TargetValue(TargetType.Const, longValue.ToString(CultureInfo.InvariantCulture));
                    return true;

                case ConditionTargetKind.Double:
                    if (value.ValueKind is not JsonValueKind.Number || !value.TryGetDouble(out var number) ||
                        double.IsNaN(number) || double.IsInfinity(number))
                    {
                        error = "The target must be a number.";
                        return false;
                    }

                    target = new TargetValue(TargetType.Const, number.ToString("R", CultureInfo.InvariantCulture));
                    return true;

                case ConditionTargetKind.Text:
                    if (value.ValueKind is not JsonValueKind.String)
                    {
                        error = "The target must be a string.";
                        return false;
                    }

                    target = new TargetValue(TargetType.Const, value.GetString());
                    return true;

                case ConditionTargetKind.TimeSpan:
                    if (value.ValueKind is not JsonValueKind.String ||
                        !System.TimeSpan.TryParse(value.GetString(), CultureInfo.InvariantCulture, out var interval))
                    {
                        error = "The target must be a TimeSpan string, e.g. \"00:30:00\".";
                        return false;
                    }

                    target = new TargetValue(TargetType.Const, interval.ToString("c", CultureInfo.InvariantCulture));
                    return true;

                case ConditionTargetKind.Version:
                    if (value.ValueKind is not JsonValueKind.String ||
                        !System.Version.TryParse(value.GetString(), out var version))
                    {
                        error = "The target must be a version string, e.g. \"1.2.3\".";
                        return false;
                    }

                    target = new TargetValue(TargetType.Const, version.ToString());
                    return true;

                default:
                    error = "Unsupported target kind.";
                    return false;
            }
        }

        private static Dictionary<string, string[]> ToErrors(Dictionary<string, List<string>> errorMap) =>
            errorMap.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());

        internal static void Add(Dictionary<string, List<string>> errors, string key, string message) =>
            (errors.TryGetValue(key, out var list) ? list : errors[key] = []).Add(message);
    }
}
