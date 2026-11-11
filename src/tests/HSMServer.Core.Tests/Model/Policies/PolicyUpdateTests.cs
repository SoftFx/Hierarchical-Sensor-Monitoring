using HSMCommon.Model;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model.Policies;
using Xunit;

namespace HSMServer.Core.Tests.Model.Policies
{
    // #1645: a PolicyUpdate whose Destination/Schedule arms are null used to
    // throw inside PolicyDestination.Update, and Policy.TryUpdate's catch
    // silently rejected the WHOLE update — callers building partial updates
    // (the test helper of TemplateApplicationTests) minted content-empty
    // policies without a whisper. Null arms mean "nothing to change" now,
    // like the schedule twin always did.
    public class PolicyUpdateTests
    {
        [Fact]
        public void TryUpdate_WithNullDestinationAndSchedule_AppliesTheContent()
        {
            var policy = Policy.BuildPolicy((byte)SensorType.Integer);

            var updated = policy.TryUpdate(ContentUpdate(), out var error);

            Assert.True(updated, error);
            var condition = Assert.Single(policy.Conditions);
            Assert.Equal(PolicyProperty.Value, condition.Property);
        }

        [Fact]
        public void TryUpdate_WithNullDestination_KeepsTheCurrentDestination()
        {
            var policy = Policy.BuildPolicy((byte)SensorType.Integer);

            Assert.True(policy.TryUpdate(ContentUpdate() with
            {
                Destination = new PolicyDestinationUpdate(mode: PolicyDestinationMode.Custom),
            }, out var firstError), firstError);

            Assert.True(policy.TryUpdate(ContentUpdate(), out var error), error);

            Assert.True(policy.Destination.IsCustom);
        }


        private static PolicyUpdate ContentUpdate() => new()
        {
            ConfirmationPeriod = 0,
            Conditions =
            [
                new PolicyConditionUpdate(
                    PolicyOperation.GreaterThan,
                    PolicyProperty.Value,
                    new TargetValue(TargetType.Const, "0")),
            ],
        };
    }
}
