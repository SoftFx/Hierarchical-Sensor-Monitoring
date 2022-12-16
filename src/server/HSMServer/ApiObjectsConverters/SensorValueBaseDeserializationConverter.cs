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

            while (readerClone.Read() || readerClone.TokenType != JsonTokenType.EndObject)
            {
                if (readerClone.TokenType != JsonTokenType.PropertyName)
                    continue;

                string propertyName = readerClone.GetString();
                if (propertyName != nameof(SensorValueBase.Type))
                    continue;

                readerClone.Read();
                var sensorType = readerClone.GetInt32();

                return (SensorType)sensorType switch
                {
                    SensorType.BooleanSensor => JsonSerializer.Deserialize<BoolSensorValue>(ref reader),
                    SensorType.IntSensor => JsonSerializer.Deserialize<IntSensorValue>(ref reader),
                    SensorType.DoubleSensor => JsonSerializer.Deserialize<DoubleSensorValue>(ref reader),
                    SensorType.StringSensor => JsonSerializer.Deserialize<StringSensorValue>(ref reader),
                    SensorType.IntegerBarSensor => JsonSerializer.Deserialize<IntBarSensorValue>(ref reader),
                    SensorType.DoubleBarSensor => JsonSerializer.Deserialize<DoubleBarSensorValue>(ref reader),
                    SensorType.FileSensor => JsonSerializer.Deserialize<FileSensorValue>(ref reader),
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
