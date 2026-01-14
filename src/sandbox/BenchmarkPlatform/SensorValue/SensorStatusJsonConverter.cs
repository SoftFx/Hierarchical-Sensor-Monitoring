using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HSMServer.Core.Model
{
    public class SensorStatusJsonConverter : JsonConverter<SensorStatus>
    {
        public override SensorStatus Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => ToStatus(reader.GetByte());

        public override void Write(Utf8JsonWriter writer, SensorStatus value, JsonSerializerOptions options) => writer.WriteNumberValue((byte)value);

        private SensorStatus ToStatus(byte status) => status switch
        {
            (byte)SensorStatus.Ok => SensorStatus.Ok,
            (byte)SensorStatus.OffTime => SensorStatus.OffTime,
            _ => SensorStatus.Error,
        };
    }
}
