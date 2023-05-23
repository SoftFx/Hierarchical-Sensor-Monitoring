using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.Model.Policies.Infrastructure
{
    public static class CustomCommentBuilder
    {
        private const char Separator = ' ';

        private readonly static ConcurrentDictionary<string, string> _properties = new();


        public static Dictionary<string, string> Properties { get; } = new()
        {
            { "$product", "Parent product name" },
            { "$path", "Sensor path" },
            { "$sensor", "Sensor name" },
            { "$action" , "Alert binary operation" },
            { "$target" , "Alert constant to compare" },
            { "$time", "Sensor value sending time" },
            { "$status", "Sensor value status" },
            { "$comment", "Sensor value comment" },
            { "$value", "Sensor value" },
            { "$min", "Bar sensor min value" },
            { "$max", "Bar sensor max value" },
            { "$mean", "Bar sensor mean value" },
            { "$lastValue", "Bar sensor lastValue value" },
        };


        static CustomCommentBuilder()
        {
            var properties = Properties.Keys.ToList();

            for (int i = 0; i < properties.Count; ++i)
                _properties.TryAdd(properties[i], $"{{{i}}}");
        }


        public static string GetSingleComment<T, U>(T value, BaseSensorModel sensor, DataPolicy<T, U> policy)
            where T : BaseValue<U>
        {
            var template = GetTemplateString(policy.Comment);

            return string.Format(template, sensor.RootProductName, sensor.Path, sensor.DisplayName, policy.Operation, policy.Target.Value,
                value.Time, value.Status, value.Comment, value.Value, null, null, null, null);
        }

        public static string GetBarComment<T, U>(T value, BaseSensorModel sensor, DataPolicy<T, U> policy)
            where T : BarBaseValue<U>
            where U : struct
        {
            var template = GetTemplateString(policy.Comment);

            return string.Format(template, sensor.RootProductName, sensor.Path, sensor.DisplayName, policy.Operation, policy.Target.Value,
                value.Time, value.Status, value.Comment, null, value.Min, value.Max, value.Mean, value.LastValue);
        }

        private static string GetTemplateString(string raw)
        {
            var words = raw.Split(Separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            for (int i = 0; i < words.Length; ++i)
            {
                ref string word = ref words[i];

                foreach (var (property, index) in _properties)
                    if (word.Contains(property))
                        word = word.Replace(property, index);
            }

            return string.Join(Separator, words);
        }
    }
}
