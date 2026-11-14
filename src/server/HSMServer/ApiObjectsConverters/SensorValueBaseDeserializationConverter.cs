using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;


namespace HSMServer.ApiObjectsConverters
{
    [JsonConverter(typeof(VersionConverter))]
    public sealed class VersionSensor : VersionSensorValue { }


    internal class VersionConverter : JsonConverter<VersionSensor>
    {
        public override VersionSensor Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => SensorValueBaseDeserializationConverter.DeserializeVersion(ref reader, options);

        public override void Write(Utf8JsonWriter writer, VersionSensor value, JsonSerializerOptions options)
        {
            // POST /api/sensors/version echoes the bound DTO back. Serialize the base
            // VersionSensorValue with the pipeline options: System.Text.Json has a built-in
            // System.Version converter (writes "1.2.3.4"), and the options carry the same
            // camelCase naming policy every other typed route answers with (#1671).
            JsonSerializer.Serialize<VersionSensorValue>(writer, value, options);
        }
    }

    internal sealed class SensorValueBaseDeserializationConverter : JsonConverter<SensorValueBase>
    {
        private const string UnexpectedSensorTypeError = "Unexpected sensor type";
        private const string UnexpectedJsonError = "Unexpected string for sensor values deserialization";


        public override SensorValueBase Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            Utf8JsonReader readerClone = reader; // readerClone is a full copy of reader, because Utf8JsonReader is ref struct

            while (readerClone.TokenType != JsonTokenType.EndObject && readerClone.TokenType != JsonTokenType.EndArray && readerClone.Read())
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
                    SensorType.VersionSensor => DeserializeVersion(ref reader, options),
                    SensorType.RateSensor => JsonSerializer.Deserialize<RateSensorValue>(ref reader, options),
                    SensorType.EnumSensor => JsonSerializer.Deserialize<EnumSensorValue>(ref reader, options),
                    _ => throw new JsonException(UnexpectedSensorTypeError),
                };
            }

            throw new JsonException(UnexpectedJsonError);
        }

        public override void Write(Utf8JsonWriter writer, SensorValueBase value, JsonSerializerOptions options)
        {
            throw new NotImplementedException();
        }


        public static VersionSensor DeserializeVersion(ref Utf8JsonReader reader, JsonSerializerOptions options)
        {
            var obj = JsonSerializer.Deserialize<JsonObject>(ref reader, options);


            // Every parse below throws a JsonException on a malformed field, so the input
            // formatter answers 400 instead of an unhandled 500 (#1671). Fields that are
            // absent keep the defaults the typed routes give the other sensor DTOs.

            string GetValue(string key)
            {
                return obj[key]?.ToString();
            }

            SensorStatus GetStatus()
            {
                var raw = GetValue(nameof(VersionSensorValue.Status));

                if (string.IsNullOrEmpty(raw))
                    return SensorStatus.Ok;

                if (!int.TryParse(raw, out var status))
                    throw new JsonException($"'{raw}' is not a valid {nameof(VersionSensorValue.Status)}");

                return (SensorStatus)Math.Max(status, 0);
            }

            DateTime GetTime()
            {
                var raw = GetValue(nameof(VersionSensorValue.Time));

                if (string.IsNullOrEmpty(raw))
                    return DateTime.UtcNow;

                if (!DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var time))
                    throw new JsonException($"'{raw}' is not a valid {nameof(VersionSensorValue.Time)}");

                return time;
            }

            Version GetVersion()
            {
                if (obj[nameof(VersionSensorValue.Value)] is JsonObject valueObj)
                {
                    int GetComponent(string name)
                    {
                        if (!int.TryParse(valueObj[name]?.ToString(), out var component))
                            throw new JsonException($"'{name}' of the version value must be an integer");

                        return Math.Max(component, 0);
                    }

                    return new Version(GetComponent(nameof(Version.Major)), GetComponent(nameof(Version.Minor)),
                        GetComponent(nameof(Version.Build)), GetComponent(nameof(Version.Revision)));
                }

                var raw = GetValue(nameof(VersionSensorValue.Value));

                if (string.IsNullOrEmpty(raw))
                    throw new JsonException("The value of a version sensor must be a version string, e.g. \"1.2.3\"");

                // TryParse covers format, argument, range and overflow failures with one
                // path: new Version(string) throws OverflowException (an ArithmeticException,
                // not an ArgumentException) for a component above int.MaxValue, which an
                // exception filter on the constructor would miss and 500 the request.
                if (!Version.TryParse(raw, out var version))
                    throw new JsonException($"'{raw}' is not a valid version string");

                return version;
            }


            return new VersionSensor()
            {
                Key = GetValue(nameof(VersionSensorValue.Key)),
                Path = GetValue(nameof(VersionSensorValue.Path)),
                Time = GetTime(),
                Status = GetStatus(),
                Comment = GetValue(nameof(VersionSensorValue.Comment)),
                Value = GetVersion(),
            };
        }
    }
}
