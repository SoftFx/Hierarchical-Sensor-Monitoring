using System;
using System.ComponentModel;
using System.Reflection;
using HSMCommon.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Model.ManagementApi.AlertTemplates;
using Xunit;

namespace HSMServer.Core.Tests.Mcp
{
    // The enum tables of the [Description] docs live as literal text in both the
    // XML summaries and the [Description]s — nothing regenerates them from the
    // enums, so a new enum member goes missing silently. Every member must keep
    // appearing in its table as "<code>=<name>" (extra sentinels such as
    // SensorType's 100=AnyType are fine — coverage is checked one way only).
    public class AlertTemplateEnumTableDocsTests
    {
        [Theory]
        [InlineData(typeof(AlertTemplateDto), nameof(AlertTemplateDto.SensorType), typeof(SensorType))]
        [InlineData(typeof(PolicyConditionDto), nameof(PolicyConditionDto.Operation), typeof(PolicyOperation))]
        [InlineData(typeof(PolicyConditionDto), nameof(PolicyConditionDto.Property), typeof(PolicyProperty))]
        [InlineData(typeof(PolicyScheduleDto), nameof(PolicyScheduleDto.RepeateMode), typeof(AlertRepeatMode))]
        public void Docs_EnumTablesCoverEveryEnumMember(Type dtoType, string propertyName, Type enumType)
        {
            var property = dtoType.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)
                ?? throw new ArgumentException($"No public property {propertyName} on {dtoType.Name}");
            var description = property.GetCustomAttribute<DescriptionAttribute>()?.Description
                ?? throw new ArgumentException($"{dtoType.Name}.{propertyName} carries no [Description]");

            foreach (var value in Enum.GetValues(enumType))
                Assert.Contains($"{Convert.ToInt32(value)}={value}", description);
        }
    }
}
