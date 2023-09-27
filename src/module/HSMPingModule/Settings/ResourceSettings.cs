using System.Text.Json;
using System.Text.Json.Serialization;

namespace HSMPingModule.Settings;

internal sealed class ResourceSettings
{
    [JsonConverter(typeof(WebSitesJsonConverter))]
    public Dictionary<string, NodeSettings> WebSites { get; set; } = new()
    {
        ["google.com"] = new NodeSettings()
    };

    public NodeSettings DefaultSiteNodeSettings { get; set; } = new()
    {
        Countries = new HashSet<string>() { "Latvia" },

        PingThresholdValueSec = 15,
        TTL = TimeSpan.FromMinutes(15),
    };


    public ResourceSettings ApplyDefaultSettings()
    {
        foreach (var (key, value) in WebSites)
        {
            if (value is null)
            {
                WebSites[key] = DefaultSiteNodeSettings;
                continue;
            }

            value.Countries ??= new HashSet<string>(DefaultSiteNodeSettings.Countries);
            value.PingThresholdValueSec ??= DefaultSiteNodeSettings.PingThresholdValueSec;
            value.TTL ??= DefaultSiteNodeSettings.TTL;
        }

        return this;
    }
}

internal class WebSitesJsonConverter : JsonConverter<Dictionary<string, NodeSettings>>
{
    public override Dictionary<string, NodeSettings> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var dictionary = new Dictionary<string, NodeSettings>();
        using var jsonDocument = JsonDocument.ParseValue(ref reader);
        foreach (var property in jsonDocument.RootElement.EnumerateObject())
        {
            dictionary.Add(property.Name, property.Value.ValueKind == JsonValueKind.Null ? null : property.Value.Deserialize<NodeSettings>());
        }

        return dictionary;
    }

    public override void Write(Utf8JsonWriter writer, Dictionary<string, NodeSettings> value, JsonSerializerOptions options)
    {
        throw new NotImplementedException();
    }
}
