using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;
using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HSMServer.ApiObjectsConverters
{
    internal sealed class SensorValueBaseDeserializationConverter : JsonConverter<SensorValueBase>
    {
        private const string UnexpectedSensorTypeError = "Unexpected sensor type";
        private const string UnexpectedJsonError = "Unexpected string for sensor values deserialization";


        public override SensorValueBase Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            Utf8JsonReader readerClone = reader; // readerClone is a full copy of reader, because Utf8JsonReader is ref struct

            while ((readerClone.TokenType is not JsonTokenType.EndObject or JsonTokenType.EndArray) && readerClone.Read())
            {
                if (readerClone.TokenType != JsonTokenType.PropertyName)
                    continue;

                string propertyName = readerClone.GetString();
                if (!string.Equals(propertyName, nameof(SensorValueBase.Type), StringComparison.InvariantCultureIgnoreCase))
                    continue;

                readerClone.Read();
                var sensorType = readerClone.GetInt32();

                return (SensorType)sensorType switch
                {
                    SensorType.BooleanSensor => JsonSerializer.Deserialize<BoolSensorValue>(ref reader, options),
                    SensorType.IntSensor => JsonSerializer.Deserialize<IntSensorValue>(ref reader, options),
                    SensorType.DoubleSensor => JsonSerializer.Deserialize<DoubleSensorValue>(ref reader, options),
                    SensorType.StringSensor => JsonSerializer.Deserialize<StringSensorValue>(ref reader, options),
                    SensorType.IntegerBarSensor => JsonSerializer.Deserialize<IntBarSensorValue>(ref reader, options),
                    SensorType.DoubleBarSensor => JsonSerializer.Deserialize<DoubleBarSensorValue>(ref reader, options),
                    SensorType.FileSensor => JsonSerializer.Deserialize<FileSensorValue>(ref reader, options),
                    SensorType.TimeSpanSensor => JsonSerializer.Deserialize<TimeSpanSensorValue>(ref reader, options),
                    SensorType.VersionSensor => JsonSerializer.Deserialize<VersionSensorValue>(ref reader, options),
                    _ => throw new JsonException(UnexpectedSensorTypeError),
                };
            }

            throw new JsonException(UnexpectedJsonError);
        }

        public override void Write(Utf8JsonWriter writer, SensorValueBase value, JsonSerializerOptions options)
        {
            throw new NotImplementedException();
        }
    }
}
