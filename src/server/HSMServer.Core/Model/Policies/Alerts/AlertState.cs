﻿using HSMCommon.Extensions;
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

        private readonly static Dictionary<string, PropertyInfo> _publicProperties = typeof(AlertState).GetProperties()
            .Where(u => Attribute.IsDefined(u, typeof(AlertVariableAttribute))).ToDictionary(k => k.Name, v => v);

        private readonly static ConcurrentDictionary<string, (string index, string property)> _variables = new();

        public static Dictionary<string, string> VariablesHelp { get; } = new();


        public string this[string propertyName]
        {
            get => _publicProperties[propertyName].GetValue(this, null).ToString();
            set => _publicProperties[propertyName].SetValue(this, value);
        }


        public AlertSystemTemplate Template { get; set; }


        [AlertVariable("$product", "Parent product name")]
        public string Product { get; init; }

        [AlertVariable("$path", "Sensor path")]
        public string Path { get; init; }

        [AlertVariable("$sensor", "Sensor name")]
        public string Sensor { get; init; }

        [AlertVariable("$unit", "Sensor unit")]
        public string Unit { get; init; }

        [AlertVariable("$status", "Sensor status")]
        public string Status { get; init; }

        [AlertVariable("$time", "Sensor value sending time")]
        public string Time { get; init; }

        [AlertVariable("$comment", "Sensor value comment")]
        public string Comment { get; init; }


        [AlertVariable("$prevStatus", "Status of the previous sensor data")]
        public string PrevStatus { get; init; }

        [AlertVariable("$prevComment", "Comment of the previous sensor data")]
        public string PrevComment { get; init; }

        [AlertVariable("$prevValue", "Value of the previous sensor data")]
        public string PrevValue { get; set; }


        [AlertVariable("$value", "Sensor Value")]
        public string ValueSingle { get; private set; }

        [AlertVariable("$emaValue", "Sensor EMA (Value) value")]
        public string EmaValueSingle { get; private set; }


        [AlertVariable("$min", "Bar sensor Min value")]
        public string MinValueBar { get; private set; }

        [AlertVariable("$max", "Bar sensor Max value")]
        public string MaxValueBar { get; private set; }

        [AlertVariable("$mean", "Bar sensor Mean value")]
        public string MeanValueBar { get; private set; }

        [AlertVariable("$firstValue", "Bar sensor FirstValue value")]
        public string FirstValueBar { get; private set; }

        [AlertVariable("$lastValue", "Bar sensor LastValue value")]
        public string LastValueBar { get; private set; }

        [AlertVariable("$count", "Bar sensor Count value")]
        public string CountBar { get; private set; }


        [AlertVariable("$emaMin", "Bar sensor EMA (Min) value")]
        public string EmaMin { get; private set; }

        [AlertVariable("$emaMax", "Bar sensor EMA (Max) value")]
        public string EmaMax { get; private set; }

        [AlertVariable("$emaMean", "Bar sensor EMA (Mean) value")]
        public string EmaMean { get; private set; }

        [AlertVariable("$emaCount", "Bar sensor EMA (Count) value")]
        public string EmaCount { get; private set; }


        [AlertVariable("$property", "Alert property")]
        public string Property { get; set; }

        [AlertVariable("$operation", "Alert operation")]
        public string Operation { get; set; }

        [AlertVariable("$target", "Alert constant to compare")]
        public string Target { get; set; }


        static AlertState()
        {
            int index = 0;

            foreach (var prop in _publicProperties.Values)
            {
                var attr = prop.GetCustomAttribute<AlertVariableAttribute>();

                if (attr is not null)
                {
                    var variable = attr.Variable;

                    _variables.TryAdd(variable, ($"{{{index++}}}", prop.Name));

                    VariablesHelp.Add(variable, attr.Description);
                }
            }

        }


        public bool HasLessThanTwoDiff(AlertState other, out string diffProp)
        {
            bool hasDiff = false;

            diffProp = string.Empty;

            if (Template.Count != other.Template.Count)
                return false;

            foreach (var prop in _publicProperties.Values)
            {
                var propName = prop.Name;

                if (UseProperty(propName) ^ other.UseProperty(propName))
                    return false;

                if (UseProperty(propName) && other.UseProperty(propName))
                {
                    var curValue = prop.GetValue(this, null);
                    var otherValue = prop.GetValue(other, null);

                    if (curValue is null && otherValue is null)
                        continue;

                    if (!curValue?.Equals(otherValue) ?? true) //protection for first null
                    {
                        diffProp = prop.Name;
                        hasDiff = !hasDiff; // true -> false mean find 2 diff

                        if (!hasDiff)
                            return false;
                    }
                }
            }

            return true;
        }


        public string BuildComment(string template = null) => string.Format(template ?? Template?.Text ?? string.Empty,
            Product, Path, Sensor, Unit, Status, Time, Comment, PrevStatus, PrevComment, PrevValue, ValueSingle, EmaValueSingle,
            MinValueBar, MaxValueBar, MeanValueBar, FirstValueBar, LastValueBar, CountBar, EmaMin, EmaMax, EmaMean, EmaCount,
            Property, Operation, GetCorrectTarget());

        public static AlertSystemTemplate BuildSystemTemplate(string raw)
        {
            var words = raw?.Split(Separator, SplitOptions) ?? Array.Empty<string>();
            var hash = new AlertSystemTemplate();

            for (int i = 0; i < words.Length; ++i)
            {
                ref string word = ref words[i];

                foreach (var (variable, (index, property)) in _variables)
                    if (word.Contains(variable))
                    {
                        word = word.Replace(variable, index);
                        hash.Add(property);
                    }
            }

            return new(hash)
            {
                Text = string.Join(Separator, words),
            };
        }

        public static AlertState Build<T>(BaseValue<T> value, BaseSensorModel sensor)
        {
            var state = BuildBase(value, sensor);

            state.ValueSingle = GetReadableValue(value);
            state.EmaValueSingle = value?.EmaValue?.ToString();

            if (sensor.LastValue is BaseValue<T> lastValue)
                state.PrevValue = GetReadableValue(lastValue);

            return state;
        }

        public static AlertState Build<T>(BarBaseValue<T> value, BaseSensorModel sensor)
            where T : struct, INumber<T>
        {
            var state = BuildBase(value, sensor);

            state.MinValueBar = value?.Min.ToString();
            state.MaxValueBar = value?.Max.ToString();
            state.MeanValueBar = value?.Mean.ToString();
            state.FirstValueBar = value?.FirstValue?.ToString();
            state.LastValueBar = value?.LastValue.ToString();
            state.CountBar = value?.Count.ToString();

            state.EmaMin = value?.EmaMin?.ToString();
            state.EmaMax = value?.EmaMax?.ToString();
            state.EmaMean = value?.EmaMean?.ToString();
            state.EmaCount = value?.EmaCount?.ToString();

            return state;
        }

        public static AlertState BuildBase(BaseValue value, BaseSensorModel sensor) => new()
        {
            Product = sensor.RootProductName,
            Sensor = sensor.DisplayName,
            Unit = sensor.OriginalUnit?.GetDisplayName(),
            Path = sensor.Path,

            PrevStatus = sensor.LastValue?.Status.ToIcon(),
            PrevComment = sensor.LastValue?.Comment,

            Status = value?.Status.ToIcon(),
            Time = value?.Time.ToString(),
            Comment = value?.Comment,
        };


        private bool UseProperty(string name) => Template?.Contains(name) ?? false;

        private string GetCorrectTarget() => Guid.TryParse(Target, out _) ? Sensor : Target; //skipping for guid

        private static string GetReadableValue<T>(BaseValue<T> value) => value switch
        {
            TimeSpanValue timeSpan => timeSpan.Value.ToReadableView(),
            _ => value?.Value?.ToString(),
        };
    }
}