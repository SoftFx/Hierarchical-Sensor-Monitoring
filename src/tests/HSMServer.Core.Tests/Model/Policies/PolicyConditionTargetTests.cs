using System;
using HSMCommon.Model;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using Xunit;
using TestSensorModelFactory = HSMServer.Core.Tests.Infrastructure.SensorModelFactory;

namespace HSMServer.Core.Tests.Model.Policies
{
    // Pins the Const-target handling of target-less operations (#1439): the
    // operations whose target the web editor never renders (IsChanged, IsError,
    // IsOk, IsChangedToError, IsChangedToOk, ReceivedNewValue — AlertExtensions
    // .IsTargetVisible) compare against the sensor's own previous value, and the
    // executors they run under (PolicyNewValueExecutor, PolicyExecutorStatus)
    // accept only that LastValue(self) builder. A Const target on such an
    // operation — schema-legal for REST/MCP clients and possible in stored
    // entities — must be reconstructed as LastValue(self), not throw
    // NotImplementedException from PolicyExecutor<T>.SetTarget.
    public class PolicyConditionTargetTests
    {
        [Fact]
        public void NewSensorDataWithConstTarget_IsReconstructedAndFires()
        {
            var sensor = BuildSensor(SensorType.DoubleBar, new DoubleBarValue { Min = 0, Max = 1, Mean = 0.5, Count = 1 });
            var policy = Policy.BuildPolicy((byte)SensorType.DoubleBar);

            var exception = Record.Exception(() => policy.Apply(BuildEntity(PolicyProperty.NewSensorData, PolicyOperation.ReceivedNewValue, TargetType.Const, "0"), sensor));

            Assert.Null(exception);
            Assert.True(((Policy<DoubleBarValue>)policy).Validate(new DoubleBarValue { Min = 1, Max = 2, Mean = 1.5, Count = 1 }));
        }

        [Fact]
        public void NewSensorDataWithEmptyConstTarget_IsReconstructedForAnyType()
        {
            // AnyType templates reconstruct as BooleanPolicy, whose bool target
            // parser throws FormatException on the empty string — the coercion
            // must not even look at the value.
            var sensor = BuildSensor(SensorType.Boolean, new BooleanValue { Value = true });
            var policy = Policy.BuildPolicy(AlertTemplateModel.AnyType);

            var exception = Record.Exception(() => policy.Apply(BuildEntity(PolicyProperty.NewSensorData, PolicyOperation.ReceivedNewValue, TargetType.Const, ""), sensor));

            Assert.Null(exception);
            Assert.True(((Policy<BooleanValue>)policy).Validate(new BooleanValue { Value = false }));
        }

        [Fact]
        public void StatusWithConstTarget_IsReconstructed()
        {
            // The same family: every Status operation is target-less, and the
            // condition's const converter produces the sensor value type (here
            // double), which PolicyExecutorStatus cannot accept.
            var sensor = BuildSensor(SensorType.Double, new DoubleValue { Value = 1 });
            var policy = Policy.BuildPolicy((byte)SensorType.Double);

            var exception = Record.Exception(() => policy.Apply(BuildEntity(PolicyProperty.Status, PolicyOperation.IsOk, TargetType.Const, "5"), sensor));

            Assert.Null(exception);
        }

        [Fact]
        public void CommentIsChangedWithConstTarget_ComparesAgainstLastValue()
        {
            // "is changed" means "differs from the sensor's own previous value"
            // (the only form the editor and the per-sensor API produce); a Const
            // target on it is evaluated with the same semantics.
            var sensor = BuildSensor(SensorType.Double, new DoubleValue { Value = 1, Comment = "before" });
            var policy = Policy.BuildPolicy((byte)SensorType.Double);
            policy.Apply(BuildEntity(PolicyProperty.Comment, PolicyOperation.IsChanged, TargetType.Const, "anything"), sensor);

            var typed = (Policy<DoubleValue>)policy;

            Assert.True(typed.Validate(new DoubleValue { Value = 2, Comment = "after" }));
            Assert.False(typed.Validate(new DoubleValue { Value = 2, Comment = "before" }));
        }

        [Fact]
        public void TargetedOperationWithConstTarget_StillComparesAgainstConstant()
        {
            var sensor = BuildSensor(SensorType.Integer, new IntegerValue { Value = 1 });
            var policy = Policy.BuildPolicy((byte)SensorType.Integer);
            policy.Apply(BuildEntity(PolicyProperty.Value, PolicyOperation.GreaterThan, TargetType.Const, "42"), sensor);

            var typed = (Policy<IntegerValue>)policy;

            Assert.True(typed.Validate(new IntegerValue { Value = 50 }));
            Assert.False(typed.Validate(new IntegerValue { Value = 10 }));
        }

        [Fact]
        public void CoercedCondition_RoundTripsThroughEntity()
        {
            var sensor = BuildSensor(SensorType.Double, new DoubleValue { Value = 1 });
            var policy = Policy.BuildPolicy((byte)SensorType.Double);
            policy.Apply(BuildEntity(PolicyProperty.NewSensorData, PolicyOperation.ReceivedNewValue, TargetType.Const, "0"), sensor);

            var entity = policy.ToEntity();
            var restored = Policy.BuildPolicy((byte)SensorType.Double);

            Assert.Null(Record.Exception(() => restored.Apply(entity, sensor)));
            Assert.Equal(TargetType.Const, (TargetType)restored.Conditions[0].Target.Type);
        }


        private static PolicyEntity BuildEntity(PolicyProperty property, PolicyOperation operation, TargetType targetType, string targetValue) => new()
        {
            Id = Guid.NewGuid().ToByteArray(),
            Conditions = [new PolicyConditionEntity
            {
                Property = (byte)property,
                Operation = (byte)operation,
                Combination = (byte)PolicyCombination.And,
                Target = new((byte)targetType, targetValue),
            }],
            Destination = new PolicyDestinationEntity(),
            Schedule = new PolicyScheduleEntity(),
        };

        private static BaseSensorModel BuildSensor(SensorType type, BaseValue seedValue)
        {
            var sensor = TestSensorModelFactory.Build(new SensorEntity
            {
                Id = Guid.NewGuid().ToString(),
                Type = (byte)type,
            });

            seedValue.Time = DateTime.UtcNow;
            sensor.TryAddValue(seedValue);

            return sensor;
        }
    }
}
