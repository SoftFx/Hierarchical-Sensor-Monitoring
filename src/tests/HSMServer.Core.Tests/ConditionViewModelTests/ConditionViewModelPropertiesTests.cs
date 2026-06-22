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
    }
}
