using System;
using HSMCommon.Model;
using HSMServer.Model.DataAlerts;
using Xunit;

namespace HSMServer.Core.Tests.ConditionViewModelTests
{
    public class ConditionViewModelPropertiesTests
    {
        [Theory]
        [InlineData(typeof(CommonConditionViewModel))]
        [InlineData(typeof(NumericConditionViewModel))]
        [InlineData(typeof(SingleConditionViewModel))]
        [InlineData(typeof(StringConditionViewModel))]
        [InlineData(typeof(VersionConditionViewModel))]
        [InlineData(typeof(FileConditionViewModel))]
        [InlineData(typeof(BarConditionViewModel))]
        [InlineData(typeof(TimeToLiveConditionViewModel))]
        public void PropertiesItems_IncludesTimeToLive(Type conditionType)
        {
            var vm = (ConditionViewModel)Activator.CreateInstance(conditionType, new object[] { true });

            Assert.Contains(vm.PropertiesItems, item => item.Value == nameof(AlertProperty.TimeToLive));
        }

        [Theory]
        [InlineData(typeof(CommonConditionViewModel))]
        [InlineData(typeof(NumericConditionViewModel))]
        [InlineData(typeof(SingleConditionViewModel))]
        [InlineData(typeof(StringConditionViewModel))]
        [InlineData(typeof(VersionConditionViewModel))]
        [InlineData(typeof(FileConditionViewModel))]
        [InlineData(typeof(BarConditionViewModel))]
        [InlineData(typeof(TimeToLiveConditionViewModel))]
        public void DefaultProperty_IsNotTimeToLive(Type conditionType)
        {
            // ConditionViewModel picks Property = PropertiesItems.First(). TimeToLive was added
            // to every condition type's Properties list as part of #1159, so this guards against
            // a future reorder making Inactivity Period the default for a freshly-created alert
            // (which would route every new row to TTLPolicy on first save).
            var vm = (ConditionViewModel)Activator.CreateInstance(conditionType, new object[] { true });

            Assert.NotEqual(AlertProperty.TimeToLive, vm.Property);
        }

        [Theory]
        // Concrete sensor types expose that type's regular property list, so a saved TTL alert
        // can be demoted back to a regular condition — #1207.
        [InlineData(SensorType.Integer, AlertProperty.Value)]
        [InlineData(SensorType.Double, AlertProperty.Value)]
        [InlineData(SensorType.Rate, AlertProperty.Value)]
        [InlineData(SensorType.Enum, AlertProperty.Value)]
        [InlineData(SensorType.Version, AlertProperty.Value)]
        [InlineData(SensorType.TimeSpan, AlertProperty.Value)]
        [InlineData(SensorType.String, AlertProperty.Length)]
        [InlineData(SensorType.File, AlertProperty.OriginalSize)]
        [InlineData(SensorType.IntegerBar, AlertProperty.Min)]
        [InlineData(SensorType.DoubleBar, AlertProperty.Max)]
        public void TTL_Condition_ExposesSensorTypeProperty(SensorType sensorType, AlertProperty expected)
        {
            var vm = new TimeToLiveConditionViewModel(sensorType, isMain: true);

            Assert.Contains(vm.PropertiesItems, item => item.Value == expected.ToString());
        }

        [Theory]
        [InlineData(null)]
        [InlineData(SensorType.Boolean)]
        public void TTL_Condition_NullOrBoolean_FallsBackToCommon(SensorType? sensorType)
        {
            // Any templates (Type = 0, falls through as null) and Boolean sensors don't have
            // type-specific regular properties — the TTL dropdown shows the Common subset only.
            var vm = new TimeToLiveConditionViewModel(sensorType, isMain: true);

            Assert.Contains(vm.PropertiesItems, item => item.Value == nameof(AlertProperty.Status));
            Assert.DoesNotContain(vm.PropertiesItems, item => item.Value == nameof(AlertProperty.Value));
        }

        [Fact]
        public void TTL_Condition_AlwaysOffersMultipleOptions()
        {
            // Regression for #1207: the dropdown must never be single-option, otherwise the user
            // cannot demote a saved TTL alert back to a regular condition.
            foreach (SensorType sensorType in Enum.GetValues(typeof(SensorType)))
            {
                var vm = new TimeToLiveConditionViewModel(sensorType, isMain: true);
                Assert.True(vm.PropertiesItems.Count > 1,
                    $"TTL condition for {sensorType} only offers {vm.PropertiesItems.Count} properties — demote is impossible.");
                Assert.Contains(vm.PropertiesItems, item => item.Value == nameof(AlertProperty.TimeToLive));
            }

            var nullVm = new TimeToLiveConditionViewModel(null, isMain: true);
            Assert.True(nullVm.PropertiesItems.Count > 1);
        }
    }
}
