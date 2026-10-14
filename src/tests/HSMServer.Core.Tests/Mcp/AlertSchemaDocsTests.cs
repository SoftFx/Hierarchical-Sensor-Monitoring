using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using HSMCommon.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Model.ManagementApi.Alerts;
using HSMServer.Model.ManagementApi.AlertTemplates;
using Xunit;

namespace HSMServer.Core.Tests.Mcp
{
    // The #1556 drift guards, the other direction of
    // AlertTemplateEnumTableDocsTests: the alert-template variable table
    // (AlertVariablesDocs.Variables) and the property x sensor-type matrix
    // (AlertPropertyMatrixDocs.Matrix) are literal text in [Description]s —
    // nothing regenerates them from the code. These tests re-derive the truth
    // from the runtime sources and fail when the docs and the code drift
    // apart in EITHER direction: the variable set from AlertState.VariablesHelp
    // (the [AlertVariable] registry the notification renderer substitutes),
    // the matrix from AlertPolicyConditionRules (the table behind both the
    // 422 validation of sensor-policy writes and the template-apply warnings).
    public class AlertSchemaDocsTests
    {
        [Theory]
        [InlineData(typeof(AlertNotificationDto), nameof(AlertNotificationDto.Template))]
        [InlineData(typeof(AlertPolicyDto), nameof(AlertPolicyDto.Template))]
        public void Docs_TemplateVariablesMatchAlertState(Type dtoType, string propertyName)
        {
            var description = GetPropertyDescription(dtoType, propertyName);

            // Every registered variable appears with its exact [AlertVariable]
            // description ("$value — Sensor Value"), so an agent never has to
            // guess a variable the state actually substitutes...
            foreach (var (variable, help) in AlertState.VariablesHelp)
                Assert.Contains($"{variable} — {help}", description);

            // ...and the table invents no variable the state does not register.
            var documented = Regex.Matches(description, @"\$[a-zA-Z]+")
                .Select(m => m.Value)
                .Distinct()
                .ToList();

            Assert.Equal(AlertState.VariablesHelp.Keys.Order(StringComparer.Ordinal), documented.Order(StringComparer.Ordinal));

            // The header keeps the renderer's sharp edges in the text
            // (AlertState.BuildSystemTemplate/BuildComment): variables match
            // as substrings and the message goes through string.Format, whose
            // braces throw or silently substitute another variable's value —
            // "any other text ships literally" was false and led agents into
            // templates that failed at evaluation time.
            Assert.Contains("substituted as substrings", description);
            Assert.Contains("rendered with string.Format", description);
        }

        [Theory]
        [InlineData(typeof(AlertConditionDto), nameof(AlertConditionDto.Property))]
        [InlineData(typeof(PolicyConditionDto), nameof(PolicyConditionDto.Property))]
        public void Docs_PropertyMatrixMatchesTheConditionRules(Type dtoType, string propertyName)
        {
            var description = GetPropertyDescription(dtoType, propertyName);

            // The matrix parses back into (sensor type, property) pairs: one
            // line per type group, " — " between the types and their
            // properties; parentheticals are prose and are skipped. Lines
            // whose left side parses as no sensor type (the header sentence)
            // are skipped too.
            var documented = new HashSet<(SensorType Type, PolicyProperty Property)>();

            foreach (var rawLine in description.Split('\n'))
            {
                var line = rawLine.Trim();

                if (!line.Contains(" — "))
                    continue;

                var separator = line.IndexOf(" — ", StringComparison.Ordinal);
                var left = line[..separator];
                var right = Regex.Replace(line[(separator + 3)..], @"\([^)]*\)", string.Empty);

                SensorType[] types = null;

                if (left == "every type")
                {
                    types = Enum.GetValues<SensorType>();
                }
                else
                {
                    var candidates = left.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(candidate => Enum.TryParse(candidate, ignoreCase: false, out SensorType type) ? type : (SensorType?)null)
                        .ToList();

                    if (candidates.All(type => type is not null))
                        types = candidates.Cast<SensorType>().ToArray();
                }

                if (types is null)
                    continue;

                foreach (var token in right.Split(new[] { ',', ' ', '.' }, StringSplitOptions.RemoveEmptyEntries))
                    if (Enum.TryParse(token, ignoreCase: false, out PolicyProperty property))
                        foreach (var type in types)
                            documented.Add((type, property));
            }

            Assert.NotEmpty(documented);

            // Exact set equality with the rule table over the full grid —
            // an offered pair missing from the docs and a non-offered pair
            // documented as offered both fail here (xUnit's collection
            // equality is order-insensitive; tuples compare by value).
            var expected = new HashSet<(SensorType Type, PolicyProperty Property)>(
                from type in Enum.GetValues<SensorType>()
                from property in Enum.GetValues<PolicyProperty>()
                where AlertPolicyConditionRules.TryGetRule(type, property, out _)
                select (type, property));

            Assert.Equal(expected, documented);
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
