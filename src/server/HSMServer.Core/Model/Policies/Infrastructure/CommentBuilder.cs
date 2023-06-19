using HSMCommon.Extensions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.Model.Policies.Infrastructure
{
    public static class CommentBuilder
    {
        private const char Separator = ' ';

        private readonly static ConcurrentDictionary<string, string> _templates = new();


        public static Dictionary<string, string> Variables { get; } = new()
        {
            { "$product", "Parent product name" },
            { "$path", "Sensor path" },
            { "$sensor", "Sensor name" },
            { "$action" , "Alert binary operation" },
            { "$target" , "Alert constant to compare" },
            { "$status", "Sensor status" },
            { "$time", "Sensor value sending time" },
            { "$comment", "Sensor value comment" },
            { "$value", "Sensor value" },
            { "$min", "Bar sensor min value" },
            { "$max", "Bar sensor max value" },
            { "$mean", "Bar sensor mean value" },
            { "$lastValue", "Bar sensor lastValue value" },
        };


        static CommentBuilder()
        {
            var properties = Variables.Keys.ToList();

            for (int i = 0; i < properties.Count; ++i)
                _templates.TryAdd(properties[i], $"{{{i}}}");
        }


        public static string GetSingleComment<T, U>(T value, BaseSensorModel sensor, DataPolicy<T, U> policy)
            where T : BaseValue<U>, new()
        {
            value ??= new();

            var template = GetTemplateString(policy.Template);

            return string.Format(template, sensor.RootProductName, sensor.Path, sensor.DisplayName, policy.Operation.GetDisplayName(), policy.Target.Value,
               value.Status, value.Time, value.Comment, value.Value, null, null, null, null);
        }

        public static string GetBarComment<T, U>(T value, BaseSensorModel sensor, DataPolicy<T, U> policy)
            where T : BarBaseValue<U>, new()
            where U : struct
        {
            value ??= new();

            var template = GetTemplateString(policy.Template);

            return string.Format(template, sensor.RootProductName, sensor.Path, sensor.DisplayName, policy.Operation.GetDisplayName(), policy.Target.Value,
                value.Status, value.Time, value.Comment, null, value.Min, value.Max, value.Mean, value.LastValue);
        }

        public static string GetTemplateString(string raw)
        {
            var words = raw?.Split(Separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? Array.Empty<string>();

            for (int i = 0; i < words.Length; ++i)
            {
                ref string word = ref words[i];

                foreach (var (property, index) in _templates)
                    if (word.Contains(property))
                        word = word.Replace(property, index);
            }

            return string.Join(Separator, words);
        }
    }
}