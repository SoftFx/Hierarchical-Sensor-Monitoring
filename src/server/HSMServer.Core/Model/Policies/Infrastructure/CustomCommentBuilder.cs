using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace HSMServer.Core.Model.Policies.Infrastructure
{
    public static class CustomCommentBuilder
    {
        private const char Separator = ' ';

        private readonly static List<string> _properties = new(1 << 4) { "time", "path", "value", "min" };


        public static ConcurrentDictionary<string, string> Properties { get; } = new();


        static CustomCommentBuilder()
        {
            for (int i = 0; i < _properties.Count; ++i)
                Properties.TryAdd($"${_properties[i]}", $"{{{i}}}");
        }


        public static string GetTemplateString(string raw)
        {
            var words = raw.Split(Separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            for (int i = 0; i < words.Length; ++i)
            {
                ref string word = ref words[i];

                if (Properties.TryGetValue(word, out var index))
                    word = index;
            }

            return string.Join(Separator, words);
        }

        internal static string GetSingleComment<T>(BaseValue<T> value, BaseSensorModel sensor, string message)
        {
            var template = GetTemplateString(message);

            return string.Format(template, value.Time, sensor.Path, value.Value, null);
        }

        internal static string GetBarComment<T>(BarBaseValue<T> value, BaseSensorModel sensor, string message) where T : struct
        {
            var template = GetTemplateString(message);

            return string.Format(template, value.Time, sensor.Path, null, value.Min);
        }
    }
}
