using HSMServer.Core.DataLayer;
using System;
using System.Text.Json;

namespace HSMServer.Core.Model
{
    public abstract record BarBaseValue : BaseValue
    {
        private const string CountPropertyName = "Count";
        private const string OpenTimePropertyName = "StartTime";
        private const string CloseTimePropertyName = "EndTime";


        public int Count { get; init; }

        public DateTime OpenTime { get; init; }

        public DateTime CloseTime { get; init; }


        protected BarBaseValue(JsonElement element) : base(element)
        {
            Count = EntityConverter.GetProperty(GetTypedData(element), CountPropertyName, element => element.GetInt32());
            OpenTime = EntityConverter.GetProperty(GetTypedData(element), OpenTimePropertyName, element => element.GetDateTime());
            CloseTime = EntityConverter.GetProperty(GetTypedData(element), CloseTimePropertyName, element => element.GetDateTime());
        }

        public BarBaseValue() : base() { }
    }


    public abstract record BarBaseValue<T> : BarBaseValue
    {
        private const string MinPropertyName = "Min";
        private const string MaxPropertyName = "Max";
        private const string MeanPropertyName = "Mean";
        private const string LastValuePropertyName = "LastValue";


        protected abstract Func<JsonElement, T> GetValuePropertyAction { get; }


        public T Min { get; init; }

        public T Max { get; init; }

        public T Mean { get; init; }

        public T LastValue { get; init; }


        protected BarBaseValue(JsonElement element) : base(element)
        {
            Min = EntityConverter.GetProperty(GetTypedData(element), MinPropertyName, GetValuePropertyAction);
            Max = EntityConverter.GetProperty(GetTypedData(element), MaxPropertyName, GetValuePropertyAction);
            Mean = EntityConverter.GetProperty(GetTypedData(element), MeanPropertyName, GetValuePropertyAction);
            LastValue = EntityConverter.GetProperty(GetTypedData(element), LastValuePropertyName, GetValuePropertyAction);
        }

        public BarBaseValue() : base() { }
    }
}
