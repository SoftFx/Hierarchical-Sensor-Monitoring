using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;

namespace HSMServer.Core.Model.Policies
{
    public record AlertState
    {
        private const StringSplitOptions SplitOptions = StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries;
        private const char Separator = ' ';

        private readonly static Dictionary<string, PropertyInfo> _publicProperties = typeof(AlertState).GetProperties(BindingFlags.Public)
                                                                                                       .ToDictionary(k => k.Name, v => v);
        private readonly static ConcurrentDictionary<string, string> _variableTemplates = new();

        public static Dictionary<string, string> Variables { get; } = new();


        public string this[string propertyName]
        {
            get => _publicProperties[propertyName].GetValue(this, null).ToString();

            set => _publicProperties[propertyName].SetValue(this, value);
        }


        [CommentVariable("$product", "Parent product name")]
        public string Product { get; init; }

        [CommentVariable("$path", "Sensor path")]
        public string Path { get; init; }

        [CommentVariable("$sensor", "Sensor name")]
        public string Sensor { get; init; }

        [CommentVariable("$status", "Sensor status")]
        public string Status { get; init; }

        [CommentVariable("$time", "Sensor value sending time")]
        public string Time { get; init; }

        [CommentVariable("$comment", "Sensor value comment")]
        public string Comment { get; init; }


        [CommentVariable("$value", "Sensor value")]
        public string ValueSingle { get; private set; }


        [CommentVariable("$min", "Bar sensor min value")]
        public string MinValueBar { get; private set; }

        [CommentVariable("$max", "Bar sensor max value")]
        public string MaxValueBar { get; private set; }

        [CommentVariable("$mean", "Bar sensor mean value")]
        public string MeanValueBar { get; private set; }

        [CommentVariable("$lastValue", "Bar sensor lastValue value")]
        public string LastValueBar { get; private set; }


        [CommentVariable("$operation", "Alert operation")]
        public string Operation { get; set; }

        [CommentVariable("$target", "Alert constant to compare")]
        public string Target { get; set; }


        static AlertState()
        {
            foreach (var prop in _publicProperties.Values)
            {
                var attr = prop.GetCustomAttribute<CommentVariableAttribute>();

                if (attr is not null)
                    Variables.Add(attr.Variable, attr.Description);
            }

            var variables = Variables.Keys.ToList();

            for (int i = 0; i < variables.Count; ++i)
                _variableTemplates.TryAdd(variables[i], $"{{{i}}}");
        }


        public bool HasLessThanTwoDiff(AlertState other, out string diffProp)
        {
            bool hasDiff = false;

            diffProp = string.Empty;

            foreach (var prop in _publicProperties.Values)
            {
                var curValue = prop.GetValue(this, null);
                var otherValue = prop.GetValue(other, null);

                if (!curValue.Equals(otherValue))
                {
                    diffProp = prop.Name;
                    hasDiff = !hasDiff; // true -> false mean find 2 diff

                    if (!hasDiff)
                        return false;
                }
            }

            return true;
        }


        internal string BuildComment(string template) => string.Format(template, Product, Path, Sensor,
            Operation, Target, Status, Time, Comment, ValueSingle, MinValueBar, MaxValueBar, MeanValueBar,
            LastValueBar);


        internal static string BuildSystemTemplate(string raw)
        {
            var words = raw?.Split(Separator, SplitOptions) ?? Array.Empty<string>();

            for (int i = 0; i < words.Length; ++i)
            {
                ref string word = ref words[i];

                foreach (var (property, index) in _variableTemplates)
                    if (word.Contains(property))
                        word = word.Replace(property, index);
            }

            return string.Join(Separator, words);
        }

        internal static AlertState Build<T>(BaseValue<T> value, BaseSensorModel sensor)
        {
            var state = BuildBase(value, sensor);

            state.ValueSingle = value.Value.ToString();

            return state;
        }

        internal static AlertState Build<T>(BarBaseValue<T> value, BaseSensorModel sensor)
            where T : struct, INumber<T>
        {
            var state = BuildBase(value, sensor);

            state.MinValueBar = value.Min.ToString();
            state.MaxValueBar = value.Max.ToString();
            state.MeanValueBar = value.Mean.ToString();
            state.LastValueBar = value.LastValue.ToString();

            return state;
        }

        private static AlertState BuildBase<T>(T value, BaseSensorModel sensor)
            where T : BaseValue => new()
            {
                Product = sensor.RootProductName,
                Sensor = sensor.DisplayName,
                Path = sensor.Path,

                Status = value.Status.ToString(),
                Time = value.Time.ToString(),
                Comment = value.Comment,
            };
    }
}