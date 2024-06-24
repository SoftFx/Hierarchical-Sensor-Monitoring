using System;
using System.Text.Json.Serialization;
using System.Text.Json;
using HSMSensorDataObjects;


namespace HSMDataCollector.Converters
{
    public class JsonCommandConverter : JsonConverter<CommandRequestBase>
    {
        public override CommandRequestBase Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            throw new NotImplementedException();
        }

        public override void Write(Utf8JsonWriter writer, CommandRequestBase value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, value, value.GetType(), options);
        }
    }
}
