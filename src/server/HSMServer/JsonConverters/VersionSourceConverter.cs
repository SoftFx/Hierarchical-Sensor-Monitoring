using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HSMServer.JsonConverters;

public class VersionSourceConverter : JsonConverter<Version>
{
    public override Version Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        throw new NotImplementedException();
    }

    public override void Write(Utf8JsonWriter writer, Version value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("major", value.Major);
        writer.WriteNumber("minor", value.Minor);
        writer.WriteNumber("build", value.Build);
        writer.WriteNumber("revision", value.Revision);
        writer.WriteNumber("majorRevision", value.MajorRevision);
        writer.WriteNumber("minorRevision", value.MinorRevision);
        writer.WriteEndObject();
    }
}