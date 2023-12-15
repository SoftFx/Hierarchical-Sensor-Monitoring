using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HSMServer.Converters;

public class DateTimeConverter : JsonConverter<DateTime?>
{
    public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var str = reader.GetString();

        if (string.IsNullOrEmpty(str))
            return null;
        
        return DateTime.TryParse(str, out var dateTime) ? dateTime : null;
    }

    public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
    {
        throw new NotImplementedException();
    }
}