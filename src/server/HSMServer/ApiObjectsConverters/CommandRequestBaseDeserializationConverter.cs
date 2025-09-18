using HSMSensorDataObjects;
using HSMServer.DTOs;
using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HSMServer.ApiObjectsConverters
{
    public sealed class CommandRequestBaseDeserializationConverter : JsonConverter<CommandRequestBase>
    {
        private const string UnexpectedRequestTypeError = "Unexpected request type";
        private const string UnexpectedJsonError = "Unexpected string for sensor values deserialization";


        public override CommandRequestBase Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            Utf8JsonReader readerClone = reader; // readerClone is a full copy of reader, because Utf8JsonReader is ref struct

            while (readerClone.TokenType != JsonTokenType.EndObject && readerClone.TokenType != JsonTokenType.EndArray && readerClone.Read())
            {
                if (readerClone.TokenType != JsonTokenType.PropertyName)
                    continue;

                string propertyName = readerClone.GetString();
                
                if (!string.Equals(propertyName, nameof(CommandRequestBase.Type), StringComparison.InvariantCultureIgnoreCase))
                    continue;

                readerClone.Read();
                var requestType = readerClone.GetInt32();

                return (Command)requestType switch
                {
                    Command.AddOrUpdateSensor => JsonSerializer.Deserialize<AddOrUpdateSensorRequestDto>(ref reader, options),
                    _ => throw new JsonException(UnexpectedRequestTypeError),
                };
            }

            throw new JsonException(UnexpectedJsonError);
        }

        public override void Write(Utf8JsonWriter writer, CommandRequestBase value, JsonSerializerOptions options)
        {
            throw new NotImplementedException();
        }
    }
}
