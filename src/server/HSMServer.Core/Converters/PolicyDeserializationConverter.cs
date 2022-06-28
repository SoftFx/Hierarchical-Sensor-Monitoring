using HSMServer.Core.Model;
using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HSMServer.Core.Converters
{
    internal sealed class PolicyDeserializationConverter : JsonConverter<Policy>
    {
        public override Policy Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            Utf8JsonReader readerClone = reader;

            while (readerClone.Read() || readerClone.TokenType != JsonTokenType.EndObject)
            {
                if (readerClone.TokenType != JsonTokenType.PropertyName)
                    continue;

                string propertyName = readerClone.GetString();
                if (propertyName != nameof(Policy.Type))
                    continue;

                readerClone.Read();
                var policyType = readerClone.GetString();

                return policyType switch
                {
                    nameof(ExpectedUpdateIntervalPolicy) => JsonSerializer.Deserialize<ExpectedUpdateIntervalPolicy>(ref reader),
                    _ => throw new JsonException("Unexpected policy type"),
                };
            }

            throw new JsonException("Unexpected string for policy deserialization");
        }

        public override void Write(Utf8JsonWriter writer, Policy value, JsonSerializerOptions options)
        {
            throw new NotImplementedException();
        }
    }
}
