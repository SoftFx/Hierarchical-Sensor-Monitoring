using HSMServer.Core.DataLayer;
using System;
using System.Text.Json;

namespace HSMServer.Core.Model
{
    //public enum SensorStatus : byte
    //{
    //    Ok,
    //    Warning,
    //    Error,
    //    Unknown = byte.MaxValue,
    //}

    public enum SensorType : byte
    {
        Boolean,
        Integer,
        Double,
        String,
        IntegerBar,
        DoubleBar,
        File,
    }


    public abstract record BaseValue
    {
        private const string TimePropertyName = "Time";
        private const string RecievingTimePropertyName = "TimeCollected";
        private const string StatusPropertyName = "Status";
        private const string CommentPropertyName = "Comment";
        private const string TypedDataPropertyName = "TypedData";


        public DateTime ReceivingTime { get; } = DateTime.UtcNow;

        public string Comment { get; init; }

        public DateTime Time { get; init; }

        // TODO: if this property is necessary
        //public SensorType Type { get; init; }

        //public SensorStatus Status { get; init; }


        protected BaseValue(JsonElement element)
        {
            Time = EntityConverter.GetProperty(element, TimePropertyName, el => el.GetDateTime());
            ReceivingTime = EntityConverter.GetProperty(element, RecievingTimePropertyName, el => el.GetDateTime());
            //Status = (SensorStatus)EntityConverter.GetProperty(element, StatusPropertyName, element => element.GetByte());
            Comment = EntityConverter.GetProperty(GetTypedData(element), CommentPropertyName, el => el.GetString());
        }

        // Public parameterless constructor for using EntityConverter.ConvertSensorData<T>
        public BaseValue() { }


        public abstract BaseValue BuildValue(JsonElement element);

        protected static JsonElement GetTypedData(JsonElement element)
        {
            var typedData = EntityConverter.GetProperty(element, TypedDataPropertyName, el => el.GetString());

            return JsonDocument.Parse(typedData).RootElement;
        }
    }


    public abstract record BaseValue<T> : BaseValue
    {
        protected abstract string ValuePropertyName { get; }

        protected abstract Func<JsonElement, T> GetValuePropertyAction { get; }

        public T Value { get; init; }


        protected BaseValue(JsonElement element) : base(element)
        {
            Value = EntityConverter.GetProperty(GetTypedData(element), ValuePropertyName, GetValuePropertyAction);
        }

        public BaseValue() : base() { }
    }
}
