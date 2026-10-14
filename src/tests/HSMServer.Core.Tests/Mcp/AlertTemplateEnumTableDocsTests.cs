using System;
using System.ComponentModel;
using System.Reflection;
using HSMCommon.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.Schedule;
using HSMServer.Model.ManagementApi.AlertSchedules;
using HSMServer.Model.ManagementApi.AlertTemplates;
using HSMServer.Model.ManagementApi.Alerts;
using Xunit;

namespace HSMServer.Core.Tests.Mcp
{
    // The enum tables of the [Description] docs live as literal text in both the
    // XML summaries and the [Description]s — nothing regenerates them from the
    // enums, so a new enum member goes missing silently. Every member must keep
    // appearing in its table as "<code>=<name>" (extra sentinels such as
    // SensorType's 100=AnyType are fine — coverage is checked one way only).
    // The name-table theory covers the string-valued policy surface the same
    // one-way way, by enum NAME containment.
    public class AlertTemplateEnumTableDocsTests
    {
        [Theory]
        [InlineData(typeof(AlertTemplateDto), nameof(AlertTemplateDto.SensorType), typeof(SensorType))]
        [InlineData(typeof(PolicyConditionDto), nameof(PolicyConditionDto.Operation), typeof(PolicyOperation))]
        [InlineData(typeof(PolicyConditionDto), nameof(PolicyConditionDto.Property), typeof(PolicyProperty))]
        [InlineData(typeof(PolicyConditionDto), nameof(PolicyConditionDto.Combination), typeof(PolicyCombination))]
        [InlineData(typeof(PolicyTargetDto), nameof(PolicyTargetDto.Type), typeof(TargetType))]
        [InlineData(typeof(AlertPolicyDto), nameof(AlertPolicyDto.SensorStatus), typeof(SensorStatus))]
        [InlineData(typeof(PolicyScheduleDto), nameof(PolicyScheduleDto.RepeateMode), typeof(AlertRepeatMode))]
        public void Docs_EnumTablesCoverEveryEnumMember(Type dtoType, string propertyName, Type enumType)
        {
            var description = GetPropertyDescription(dtoType, propertyName);

            foreach (var value in Enum.GetValues(enumType))
                Assert.Contains($"{Convert.ToInt32(value)}={value}", description);
        }

        [Fact]
        public void Docs_TtlIntervalTableCoversEveryTimeIntervalMember()
        {
            // The TTL table sits on the TimeIntervalDto RECORD's [Description]
            // (the Interval property points at it); the enum is long-backed, so
            // the codes go through Convert.ToInt64.
            var description = typeof(TimeIntervalDto).GetCustomAttribute<DescriptionAttribute>()?.Description
                ?? throw new ArgumentException("TimeIntervalDto carries no [Description]");

            foreach (var value in Enum.GetValues(typeof(HSMServer.Core.Model.TimeInterval)))
                Assert.Contains($"{Convert.ToInt64(value)}={value}", description);
        }

        [Theory]
        [InlineData(typeof(AlertNotificationDto), nameof(AlertNotificationDto.Repeat), typeof(AlertRepeatMode))]
        [InlineData(typeof(AlertDestinationDto), nameof(AlertDestinationDto.Mode), typeof(PolicyDestinationMode))]
        public void Docs_EnumNameTablesCoverEveryEnumMember(Type dtoType, string propertyName, Type enumType)
        {
            var description = GetPropertyDescription(dtoType, propertyName);

            foreach (var value in Enum.GetValues(enumType))
                Assert.Contains(value.ToString(), description);
        }

        [Fact]
        public void Docs_ScheduleYamlExampleParses()
        {
            // The worked example is what an agent copies verbatim; keep it
            // parsable by the real parser. The verbatim const carries the
            // source file's line endings, so the cut goes after the first
            // newline following "Example:" (CRLF and LF alike).
            var markerIndex = AlertScheduleYamlDocs.Format.IndexOf("Example:", StringComparison.Ordinal);
            Assert.True(markerIndex >= 0, "AlertScheduleYamlDocs.Format carries no Example block");
            var newlineIndex = AlertScheduleYamlDocs.Format.IndexOf('\n', markerIndex);
            Assert.True(newlineIndex >= 0, "The Example block is empty");
            var example = AlertScheduleYamlDocs.Format[(newlineIndex + 1)..];

            var schedule = new AlertScheduleParser().Parse(example);

            Assert.NotNull(schedule);
            Assert.Equal(2, schedule.DaySchedules.Count);
            Assert.Single(schedule.DaySchedules[0].Windows);
        }

        private static string GetPropertyDescription(Type dtoType, string propertyName)
        {
            var property = dtoType.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)
                ?? throw new ArgumentException($"No public property {propertyName} on {dtoType.Name}");
            return property.GetCustomAttribute<DescriptionAttribute>()?.Description
                ?? throw new ArgumentException($"{dtoType.Name}.{propertyName} carries no [Description]");
        }
    }
}
