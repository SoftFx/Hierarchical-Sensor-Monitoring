using HSMServer.Core.Model;
using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HSMServer.Core.Converters
{
    internal sealed class PolicyDeserializationConverter : JsonConverter<Policy>
    {
        private const string UnexpectedPolicyTypeError = "Unexpected policy type";
        private const string UnexpectedJsonError = "Unexpected string for policy deserialization";


        public override Policy Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            Utf8JsonReader readerClone = reader; // readerClone is a full copy of reader, because Utf8JsonReader is ref struct

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
                    nameof(StringValueLengthPolicy) => JsonSerializer.Deserialize<StringValueLengthPolicy>(ref reader),
                    _ => throw new JsonException(UnexpectedPolicyTypeError),
                };
            }

            throw new JsonException(UnexpectedJsonError);
        }

        public override void Write(Utf8JsonWriter writer, Policy value, JsonSerializerOptions options)
        {
            throw new NotImplementedException();
        }
    }
}
