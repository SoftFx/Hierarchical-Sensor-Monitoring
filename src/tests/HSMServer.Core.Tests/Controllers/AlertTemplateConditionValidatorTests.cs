using System;
using System.Collections.Generic;
using System.Linq;
using HSMCommon.Model;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Model.ManagementApi.AlertTemplates;
using HSMServer.Model.ManagementApi.Alerts;
using Xunit;

namespace HSMServer.Core.Tests.Controllers
{
    // Drift guards of the template write's condition pre-validation (#1611).
    // AlertTemplateConditionValidator replaced the domain reconstruction as
    // the template write's gate for conditions, so its accept/reject verdict
    // must EQUAL the reconstruction's verdict for every (sensor type,
    // property, operation) cell: a divergence is either a new raw-text 400
    // (the validator accepts what the domain throws on) or a regression of
    // previously-stored writes (the validator rejects what the domain
    // builds). The grid probes the REAL domain — Policy.BuildPolicy +
    // TryUpdate, the same seam the per-sensor write uses — so any domain
    // change (a new executor, a widened operation set) fails here until the
    // validator's table is brought along.
    public class AlertTemplateConditionValidatorTests
    {
        [Fact]
        public void ConditionVerdictsMatchTheDomainReconstruction()
        {
            // AnyType templates reconstruct against a Boolean stand-in — the
            // probe uses the same class, the verdicts must agree with it.
            var probeTypes = new (byte TemplateType, SensorType DomainType)[]
            {
                (AlertTemplateModel.AnyType, SensorType.Boolean),
                ((byte)SensorType.Boolean, SensorType.Boolean),
                ((byte)SensorType.Integer, SensorType.Integer),
                ((byte)SensorType.Double, SensorType.Double),
                ((byte)SensorType.Rate, SensorType.Rate),
                ((byte)SensorType.Enum, SensorType.Enum),
                ((byte)SensorType.TimeSpan, SensorType.TimeSpan),
                ((byte)SensorType.Version, SensorType.Version),
                ((byte)SensorType.String, SensorType.String),
                ((byte)SensorType.File, SensorType.File),
                ((byte)SensorType.IntegerBar, SensorType.IntegerBar),
                ((byte)SensorType.DoubleBar, SensorType.DoubleBar),
            };

            var mismatches = new List<string>();

            foreach (var (templateType, domainType) in probeTypes)
                foreach (var property in Enum.GetValues<PolicyProperty>())
                    foreach (var operation in Enum.GetValues<PolicyOperation>())
                    {
                        var condition = new PolicyConditionDto
                        {
                            Target = SampleTarget(domainType, property, operation),
                            Operation = (byte)operation,
                            Property = (byte)property,
                        };

                        var validatorAccepts = AlertTemplateConditionValidator.TryValidateCondition(
                            templateType, condition, out _, out _);
                        var domainAccepts = DomainAccepts(domainType, property, operation, condition.Target);

                        if (validatorAccepts != domainAccepts)
                            mismatches.Add(
                                $"{templateType}/{property}/{operation}: validator {(validatorAccepts ? "accepts" : "rejects")}, domain {(domainAccepts ? "accepts" : "rejects")}");
                    }

            Assert.True(mismatches.Count == 0,
                $"{mismatches.Count} verdict mismatches:\n{string.Join("\n", mismatches)}");
        }

        [Fact]
        public void TtlConditionVerdictsMatchTheDomainReconstruction()
        {
            // DefaultPolicyBase.GetCondition throws for every property, so
            // the domain rejects every condition on a TTL policy — the
            // validator's blanket rejection must match.
            var ttl = new TTLPolicy();

            foreach (var property in Enum.GetValues<PolicyProperty>())
                foreach (var operation in Enum.GetValues<PolicyOperation>())
                {
                    var condition = new PolicyConditionDto
                    {
                        Target = SampleTarget(SensorType.Double, property, operation),
                        Operation = (byte)operation,
                        Property = (byte)property,
                    };

                    var errors = new Dictionary<string, List<string>>();
                    AlertTemplateConditionValidator.AddConditionErrors(
                        BuildTemplate((byte)SensorType.Double, isTtl: true, condition), errors);

                    Assert.False(DomainAccepts(ttl, property, operation, condition.Target),
                        $"the domain unexpectedly accepts {property}/{operation} on a TTL policy");
                    Assert.Contains($"ttlPolicies[0].conditions[0]", errors);
                    Assert.Equal("TTL policies do not support conditions.", errors.Values.Single().Single());
                }
        }

        [Fact]
        public void MatrixDocs_QuoteTheFieldKeyedExample()
        {
            // The property-matrix header quotes the flattened error of a
            // Count condition on a Double template verbatim — this pins the
            // quote to the actual message, in both directions.
            var errors = new Dictionary<string, List<string>>();
            AlertTemplateConditionValidator.AddConditionErrors(
                BuildTemplate((byte)SensorType.Double, isTtl: false, new PolicyConditionDto
                {
                    Target = new PolicyTargetDto { Type = (byte)TargetType.Const, Value = "1" },
                    Operation = (byte)PolicyOperation.GreaterThan,
                    Property = (byte)PolicyProperty.Count,
                }),
                errors);

            var flattened = $"{errors.Keys.Single()}: {errors.Values.Single().Single()}";

            Assert.Equal("policies[0].conditions[0].property: Property 'Count' is not supported by Double templates. Supported: Value, EmaValue, Status, Comment, NewSensorData.",
                flattened);
            Assert.Contains(flattened, AlertPropertyMatrixDocs.Matrix);
        }

        [Theory]
        [InlineData((byte)SensorType.Integer, (byte)PolicyProperty.Value, "abc", "The target must be an integer number.")]
        [InlineData((byte)SensorType.Double, (byte)PolicyProperty.Value, "abc", "The target must be a number.")]
        [InlineData((byte)SensorType.Double, (byte)PolicyProperty.Value, "NaN", "The target must be a number.")]
        [InlineData((byte)SensorType.TimeSpan, (byte)PolicyProperty.Value, "abc", "The target must be a TimeSpan string, e.g. \"00:30:00\".")]
        [InlineData((byte)SensorType.Version, (byte)PolicyProperty.Value, "xx", "The target must be a version string, e.g. \"1.2.3\".")]
        [InlineData((byte)SensorType.String, (byte)PolicyProperty.Length, "abc", "The target must be an integer number.")]
        [InlineData((byte)SensorType.IntegerBar, (byte)PolicyProperty.Count, "1e3", "The target must be an integer number.")]
        [InlineData((byte)SensorType.File, (byte)PolicyProperty.OriginalSize, "9e99", "The target must be an integer number.")]
        public void UnparsableTargets_AnswerThePolicySurfaceMessageCatalog(byte sensorType, byte property,
            string target, string expected)
        {
            var errors = new Dictionary<string, List<string>>();
            AlertTemplateConditionValidator.AddConditionErrors(
                BuildTemplate(sensorType, isTtl: false, new PolicyConditionDto
                {
                    Target = new PolicyTargetDto { Type = (byte)TargetType.Const, Value = target },
                    Operation = (byte)PolicyOperation.GreaterThan,
                    Property = property,
                }),
                errors);

            Assert.Equal([expected], errors["policies[0].conditions[0].target"]);
        }

        [Theory]
        [InlineData((byte)SensorType.Integer, (byte)PolicyProperty.Value, (byte)PolicyOperation.GreaterThan, "42")]
        [InlineData((byte)SensorType.Double, (byte)PolicyProperty.Value, (byte)PolicyOperation.GreaterThan, "4.5e1")]
        [InlineData((byte)SensorType.Double, (byte)PolicyProperty.EmaValue, (byte)PolicyOperation.LessThan, "")]
        [InlineData((byte)SensorType.TimeSpan, (byte)PolicyProperty.Value, (byte)PolicyOperation.GreaterThan, "00:30:00")]
        [InlineData((byte)SensorType.Version, (byte)PolicyProperty.Value, (byte)PolicyOperation.Equal, "1.2.3")]
        [InlineData((byte)SensorType.String, (byte)PolicyProperty.Value, (byte)PolicyOperation.Contains, "anything")]
        [InlineData((byte)SensorType.String, (byte)PolicyProperty.Length, (byte)PolicyOperation.GreaterThan, "100")]
        [InlineData((byte)SensorType.IntegerBar, (byte)PolicyProperty.Count, (byte)PolicyOperation.GreaterThan, "7")]
        [InlineData((byte)SensorType.File, (byte)PolicyProperty.OriginalSize, (byte)PolicyOperation.LessThanOrEqual, "9223372036854775807")]
        [InlineData((byte)SensorType.Version, (byte)PolicyProperty.Value, (byte)PolicyOperation.GreaterThan, "1.2.3")]
        [InlineData((byte)SensorType.String, (byte)PolicyProperty.Value, (byte)PolicyOperation.IsChanged, "ignored")]
        // NewSensorData's executor is operation-blind but takes only the
        // LastValue getter: a target-less operation accepts any target (the
        // constant is ignored, coerced to LastValue(self)).
        [InlineData((byte)SensorType.Integer, (byte)PolicyProperty.NewSensorData, (byte)PolicyOperation.ReceivedNewValue, "0")]
        [InlineData((byte)SensorType.Boolean, (byte)PolicyProperty.NewSensorData, (byte)PolicyOperation.ReceivedNewValue, "True")]
        public void ParsableTargets_AreAccepted(byte sensorType, byte property, byte operation, string target)
        {
            var errors = new Dictionary<string, List<string>>();
            AlertTemplateConditionValidator.AddConditionErrors(
                BuildTemplate(sensorType, isTtl: false, new PolicyConditionDto
                {
                    Target = new PolicyTargetDto { Type = (byte)TargetType.Const, Value = target },
                    Operation = operation,
                    Property = property,
                }),
                errors);

            Assert.Empty(errors);
        }

        [Fact]
        public void NewSensorData_TargetedOperation_TakesOnlyTheLastValueTarget()
        {
            // The domain's PolicyNewValueExecutor is typed BaseValue: a Const
            // target on a TARGETED operation hands it a typed const builder
            // (Func<int>, Func<bool>, ...) it cannot run — the reconstruction
            // used to die on "Not supported alert target type Func`1". The
            // validator rejects the constant with a readable target-keyed
            // message (whatever it would parse as) and keeps accepting the
            // LastValue target the executor is built for.
            var constTarget = new PolicyConditionDto
            {
                Target = new PolicyTargetDto { Type = (byte)TargetType.Const, Value = "42" },
                Operation = (byte)PolicyOperation.GreaterThan,
                Property = (byte)PolicyProperty.NewSensorData,
            };

            var errors = new Dictionary<string, List<string>>();
            AlertTemplateConditionValidator.AddConditionErrors(
                BuildTemplate((byte)SensorType.Integer, isTtl: false, constTarget), errors);

            Assert.Equal(["Property 'NewSensorData' takes no constant target — it compares against the sensor's own values; use the sensor's last value."],
                errors["policies[0].conditions[0].target"]);

            var lastValueTarget = constTarget with
            {
                Target = new PolicyTargetDto { Type = (byte)TargetType.LastValue },
            };

            errors = new Dictionary<string, List<string>>();
            AlertTemplateConditionValidator.AddConditionErrors(
                BuildTemplate((byte)SensorType.Integer, isTtl: false, lastValueTarget), errors);

            Assert.Empty(errors);
        }


        // The probe: the same public seam the per-sensor write drives —
        // build the domain policy of the type and feed it one condition;
        // TryUpdate catches every reconstruction throw, so a false verdict
        // means the domain would have rejected the pair.
        private static bool DomainAccepts(SensorType domainType, PolicyProperty property,
            PolicyOperation operation, PolicyTargetDto target)
        {
            var policy = Policy.BuildPolicy((byte)domainType);

            return policy is not null && DomainAccepts(policy, property, operation, target);
        }

        private static bool DomainAccepts(Policy policy, PolicyProperty property,
            PolicyOperation operation, PolicyTargetDto target)
        {
            var update = new PolicyUpdate
            {
                Conditions = [new PolicyConditionUpdate(operation, property,
                    new TargetValue((TargetType)target.Type, target.Value))],
                Destination = new PolicyDestinationUpdate(),
                Schedule = new PolicyScheduleUpdate(),
            };

            return policy.TryUpdate(update, out _);
        }

        // A constant the pair's target kind parses — the grid probes
        // (type, property, operation) acceptance, so the target must not be
        // the thing that rejects. Target-less operations never parse their
        // constant; pairs without a rule reject on property grounds before
        // any parse, so their sample is irrelevant.
        private static PolicyTargetDto SampleTarget(SensorType domainType, PolicyProperty property,
            PolicyOperation operation)
        {
            if (PolicyCondition.IsTargetless(operation))
                return new PolicyTargetDto { Type = (byte)TargetType.Const, Value = "0" };

            var sample = AlertPolicyConditionRules.TryGetRule(domainType, property, out var rule)
                ? rule.TargetKind switch
                {
                    ConditionTargetKind.Integer or ConditionTargetKind.Long => "5",
                    ConditionTargetKind.Double => "5.5",
                    ConditionTargetKind.Text => "sample",
                    ConditionTargetKind.TimeSpan => "00:30:00",
                    ConditionTargetKind.Version => "1.2.3",
                    _ => "0",
                }
                : "0";

            return new PolicyTargetDto { Type = (byte)TargetType.Const, Value = sample };
        }

        // The minimal body AddConditionErrors reads: one condition placed in
        // the regular or the TTL policy list. The Ttls parity the
        // controller-level structural pass checks is irrelevant here.
        private static AlertTemplateDto BuildTemplate(byte sensorType, bool isTtl, PolicyConditionDto condition) => new()
        {
            Name = "probe",
            SensorType = sensorType,
            FolderId = Guid.NewGuid(),
            Paths = ["*/cpu"],
            Policies = isTtl ? [] : [new AlertPolicyDto { Conditions = [condition] }],
            TtlPolicies = isTtl ? [new AlertPolicyDto { Conditions = [condition] }] : [],
        };
    }
}
