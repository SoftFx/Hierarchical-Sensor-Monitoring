using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HSMServer.ApiObjectsConverters
{
    public class ListAsJsonStringConverter : ItemAsJsonStringConverter<List<string>> { }


    public abstract class ItemAsJsonStringConverter<T> : JsonConverter<T>
    {
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            throw new NotSupportedException();
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions _)
        {
            writer.WriteRawValue(JsonSerializer.Serialize(value));
        }
    }
}