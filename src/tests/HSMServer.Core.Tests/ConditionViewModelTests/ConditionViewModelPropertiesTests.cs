using System;
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
        public void DefaultProperty_IsNotTimeToLive(Type conditionType)
        {
            // ConditionViewModel picks Property = PropertiesItems.First(). TimeToLive was added
            // to every condition type's Properties list as part of #1159, so this guards against
            // a future reorder making Inactivity Period the default for a freshly-created alert
            // (which would route every new row to TTLPolicy on first save).
            var vm = (ConditionViewModel)Activator.CreateInstance(conditionType, new object[] { true });

            Assert.NotEqual(AlertProperty.TimeToLive, vm.Property);
        }
    }
}
