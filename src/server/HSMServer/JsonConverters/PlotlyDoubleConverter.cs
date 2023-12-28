using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HSMServer.JsonConverters;

public class PlotlyDoubleConverter: JsonConverter<double>
{
    public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.GetDouble();
    }

    public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString("0.#####", System.Globalization.CultureInfo.InvariantCulture));
    }
}