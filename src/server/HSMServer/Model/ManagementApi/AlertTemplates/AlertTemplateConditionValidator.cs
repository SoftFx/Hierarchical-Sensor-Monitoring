using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HSMCommon.Model;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Model.ManagementApi.Alerts;

namespace HSMServer.Model.ManagementApi.AlertTemplates
{
    // The template write's per-condition validation (#1611) — the templates'
    // twin of AlertPolicyConditionValidator: the same rule table the
    // sensor-policy surface 422s with, applied to the template's sensor
    // type BEFORE the domain reconstruction. Until #1611 these inputs
    // surfaced the reconstruction's raw exception text under a bare
    // `policies` key ("The template could not be parsed: Not supported
    // property Value for IntegerBarPolicy", FormatException strings, and an
    // AnyType template naming its Boolean stand-in instead of AnyType);
    // now they answer the field-keyed messages of the sensor-policy surface
    // (policies[i].conditions[j].property/operation/target).
    //
    // ACCEPTANCE stays the domain reconstruction's, not the rule table's:
    // the template write has always stored exactly what the domain can
    // build, so the operation sets below follow the domain executors,
    // which diverge from the per-sensor rule table in four places —
    //   * Enum's EmaValue is offered by the table but has no typed
    //     executor at all (PolicyExecutorBuilder);
    //   * Version's Value runs the six comparisons in the domain while the
    //     table offers the five text operations instead (the reverse gap
    //     of the policy surface, #1608 territory);
    //   * String's Value additionally runs IsChanged in the domain, which
    //     the table's text set omits;
    //   * NewSensorData's executor is operation-blind — every operation
    //     code runs (the table's single ReceivedNewValue is the editor's
    //     choice, not the executor's limit) — but it is typed BaseValue,
    //     so it takes only the LastValue getter: a Const target on a
    //     TARGETED operation is rejected outright (see TryValidateCondition).
    // AlertTemplateConditionValidatorTests pins this equivalence against
    // the real domain over the full (type x property x operation) grid —
    // change the domain and the guard forces this table along.
    internal static class AlertTemplateConditionValidator
    {
        // The six comparisons every numeric-style executor runs
        // (GetNumberOperation / GetNullableDoubleOperation /
        // GetTimeSpanOperation / GetVersionOperation).
        private static readonly PolicyOperation[] Comparisons =
        [
            PolicyOperation.LessThanOrEqual, PolicyOperation.LessThan, PolicyOperation.GreaterThan,
            PolicyOperation.GreaterThanOrEqual, PolicyOperation.Equal, PolicyOperation.NotEqual,
        ];

        // GetStringOperation's set (Comment and String Value): the text
        // comparisons plus IsChanged.
        private static readonly PolicyOperation[] TextOperations =
        [
            PolicyOperation.Equal, PolicyOperation.NotEqual, PolicyOperation.Contains,
            PolicyOperation.StartsWith, PolicyOperation.EndsWith, PolicyOperation.IsChanged,
        ];

        // Validates every condition of a template's policy lists, adding
        // field-keyed errors under each policy's prefix. Conditions the
        // structural pass has already rejected (null entries, missing
        // targets, undefined enum bytes) are skipped — their errors are in
        // the map already and the write fails on them.
        public static void AddConditionErrors(AlertTemplateDto dto, Dictionary<string, List<string>> errors)
        {
            foreach (var (policy, index) in (dto.Policies ?? []).Select((p, i) => (p, i)))
                AddConditionErrors(dto.SensorType, isTtlPolicy: false, policy, $"policies[{index}]", errors);

            foreach (var (policy, index) in (dto.TtlPolicies ?? []).Select((p, i) => (p, i)))
                AddConditionErrors(dto.SensorType, isTtlPolicy: true, policy, $"ttlPolicies[{index}]", errors);
        }

        private static void AddConditionErrors(int templateSensorType, bool isTtlPolicy,
            AlertPolicyDto policy, string prefix, Dictionary<string, List<string>> errors)
        {
            foreach (var (condition, index) in (policy?.Conditions ?? []).Select((c, i) => (c, i)))
            {
                var conditionPrefix = $"{prefix}.conditions[{index}]";

                if (condition is null || condition.Target is null ||
                    !Enum.IsDefined<PolicyProperty>((PolicyProperty)condition.Property) ||
                    !Enum.IsDefined<PolicyOperation>((PolicyOperation)condition.Operation))
                    continue; // the structural pass has already rejected these

                // TTL policies carry no conditions in the domain
                // (DefaultPolicyBase.GetCondition throws for every
                // property); the web UI never submits any — the interval
                // rides the parallel ttls list instead. The structural
                // pass (#1608) already reports the whole list under
                // "{prefix}.conditions" — only add the per-item message
                // when it has not, so the same defect is not reported
                // under two keys.
                if (isTtlPolicy)
                {
                    if (!errors.ContainsKey($"{prefix}.conditions"))
                        Add(errors, conditionPrefix, "TTL policies do not support conditions.");
                    continue;
                }

                if (!TryValidateCondition(templateSensorType, condition, out var key, out var message))
                    Add(errors, $"{conditionPrefix}.{key}", message);
            }
        }

        // One condition against the rule table. The out key is the field
        // SUFFIX within the condition (property/operation/target); the
        // walkers above compose the full policies[i].conditions[j].key.
        public static bool TryValidateCondition(int templateSensorType, PolicyConditionDto condition,
            out string key, out string message)
        {
            // int since #1607: the DTO's SensorType is int on the wire, and
            // this pass runs alongside structural errors, so an out-of-range
            // value can reach here — the rule lookup then misses and the
            // message names the raw value, while the structural pass carries
            // the sensorType error itself.

            var property = (PolicyProperty)condition.Property;
            var operation = (PolicyOperation)condition.Operation;

            // An AnyType template matches every sensor type; the
            // reconstruction validates it against a Boolean stand-in, so
            // only the common subset passes. The lookup reuses the empty
            // Boolean row (its per-type table is empty, the common
            // fallback remains), but the message names AnyType — the
            // template's real type — never the stand-in (#1611).
            var isAnyType = templateSensorType == AlertTemplateModel.AnyType;
            var sensorType = isAnyType ? SensorType.Boolean : (SensorType)templateSensorType;

            if (!AlertPolicyConditionRules.TryGetRule(sensorType, property, out var rule))
            {
                key = "property";
                message = $"Property '{property}' is not supported by {(isAnyType ? "AnyType" : sensorType.ToString())} templates. " +
                          $"Supported: {AlertPolicyConditionRules.DescribeSupportedProperties(sensorType)}.";
                return false;
            }

            // The one table-offered pair the domain reconstruction cannot
            // build: Enum's EmaValue has no typed executor — reject it
            // here instead of surfacing the raw throw.
            if (sensorType is SensorType.Enum && property is PolicyProperty.EmaValue)
            {
                key = "property";
                message = $"Property '{property}' is not supported by Enum templates (no typed executor). " +
                          $"Supported: {DescribeEnumTemplateProperties()}.";
                return false;
            }

            var supportedOperations = SupportedOperations(sensorType, property);

            if (supportedOperations is not null && !supportedOperations.Contains(operation))
            {
                key = "operation";
                message = $"Operation '{operation}' is not supported for property '{property}'. " +
                          $"Supported: {string.Join(", ", supportedOperations)}.";
                return false;
            }

            // Target shape: a LastValue target is never parsed, and a Const
            // target on a target-less operation is evaluated as the sensor's
            // own previous value (#1439) — neither parses anything. A Const
            // target on a targeted operation parses by the property's target
            // kind; the message catalogue is the sensor-policy surface's,
            // verbatim.
            if (condition.Target.Type == (byte)TargetType.Const && !PolicyCondition.IsTargetless(operation))
            {
                // The one targeted-operation Const the domain cannot take:
                // NewSensorData's executor is typed BaseValue
                // (PolicyNewValueExecutor), so its SetTarget accepts only the
                // LastValue getter — a typed const builder (Func<int>,
                // Func<bool>, Func<byte[]>...) answers 'Not supported alert
                // target type' in the reconstruction, whatever the constant
                // would parse as. Reject it here with a readable message.
                if (property is PolicyProperty.NewSensorData)
                {
                    key = "target";
                    message = "Property 'NewSensorData' takes no constant target — it compares against the sensor's own values; use the sensor's last value.";
                    return false;
                }

                var targetError = TargetError(rule.TargetKind, property, condition.Target.Value);

                if (targetError is not null)
                {
                    key = "target";
                    message = targetError;
                    return false;
                }
            }

            key = null;
            message = null;

            return true;
        }

        // The operations the DOMAIN executors run for the pair — null means
        // "no limit" (PolicyNewValueExecutor is operation-blind: it answers
        // true whenever a new value arrives, whatever the operation code
        // says). Otherwise the rule table's own sets, except where the
        // domain differs (see the class header): Version's Value runs the
        // six comparisons — none of the table's text operations — and
        // String's Value also runs IsChanged.
        private static PolicyOperation[] SupportedOperations(SensorType sensorType, PolicyProperty property)
        {
            if (property is PolicyProperty.NewSensorData)
                return null;

            if (property is PolicyProperty.Value && sensorType is SensorType.Version)
                return Comparisons;

            if (property is PolicyProperty.Value && sensorType is SensorType.String)
                return TextOperations;

            return AlertPolicyConditionRules.TryGetRule(sensorType, property, out var rule)
                ? rule.Operations
                : [];
        }

        // The constant of a targeted Const condition parses by the
        // property's target kind (the domain's TargetConstConverter).
        // Acceptance follows the domain's double.Parse exactly: NaN and
        // Infinity parse (the per-sensor surface rejects them only because
        // JSON cannot express them as numbers), and templates stored with
        // them before #1611 must keep round-tripping through GET->PUT — a
        // comparison against them simply never fires.
        private static string TargetError(ConditionTargetKind kind, PolicyProperty property, string value) =>
            kind switch
            {
                // The const converter is the identity — any string parses.
                ConditionTargetKind.Text => null,

                // EMA properties parse an empty constant to a null target
                // (PolicyNullableDoubleCondition) — keep accepting it.
                ConditionTargetKind.Double when IsNullableDouble(property) && string.IsNullOrEmpty(value) => null,

                ConditionTargetKind.Double => double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands,
                        CultureInfo.InvariantCulture, out _)
                        ? null
                        : "The target must be a number.",

                ConditionTargetKind.Integer => int.TryParse(value, NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out _)
                    ? null
                    : "The target must be an integer number.",

                ConditionTargetKind.Long => long.TryParse(value, NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out _)
                    ? null
                    : "The target must be an integer number.",

                ConditionTargetKind.TimeSpan => System.TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out _)
                    ? null
                    : "The target must be a TimeSpan string, e.g. \"00:30:00\".",

                ConditionTargetKind.Version => Version.TryParse(value, out _)
                    ? null
                    : "The target must be a version string, e.g. \"1.2.3\".",

                _ => null,
            };

        private static bool IsNullableDouble(PolicyProperty property) =>
            property is PolicyProperty.EmaValue or PolicyProperty.EmaMin or PolicyProperty.EmaMax or
            PolicyProperty.EmaMean or PolicyProperty.EmaCount;

        // DescribeSupportedProperties(Enum) lists the table's row — EmaValue
        // included — but the template write rejects that one pair; quote
        // the list an Enum template can actually use.
        private static string DescribeEnumTemplateProperties() => string.Join(", ",
            AlertPolicyConditionRules.DescribeSupportedProperties(SensorType.Enum)
                .Split(", ", StringSplitOptions.RemoveEmptyEntries)
                .Where(candidate => candidate != nameof(PolicyProperty.EmaValue)));

        private static void Add(Dictionary<string, List<string>> errors, string key, string message) =>
            (errors.TryGetValue(key, out var list) ? list : errors[key] = []).Add(message);
    }
}
