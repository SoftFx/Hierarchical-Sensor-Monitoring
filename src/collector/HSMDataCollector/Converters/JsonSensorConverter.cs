using System;
using System.Text.Json.Serialization;
using System.Text.Json;
using HSMSensorDataObjects.SensorValueRequests;


namespace HSMDataCollector.Converters
{
    public class JsonSensorConverter : JsonConverter<SensorValueBase>
    {
        public override SensorValueBase Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            throw new NotImplementedException();
        }

        public override void Write(Utf8JsonWriter writer, SensorValueBase value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, value, value.GetType(), options);
        }
    }
}
