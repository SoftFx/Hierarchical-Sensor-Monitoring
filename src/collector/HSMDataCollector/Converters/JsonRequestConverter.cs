using System;
using System.Text.Json.Serialization;
using System.Text.Json;


namespace HSMDataCollector.Converters
{
    public class JsonRequestConverter<T> : JsonConverter<T>
    {
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            throw new NotImplementedException();
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            // A registration without previous paths (#1724) leaves the member out, so it stays
            // byte-identical to the wire of collectors that predate sensor move and the server
            // contract tests pinning it. The DTO assembly has no System.Text.Json reference (no
            // [JsonIgnore]) and System.Text.Json 6 (the net6 target) has no contract customization,
            // hence the node round trip — registrations are rare (start and re-registration only).
            if (value is HSMSensorDataObjects.SensorRequests.AddOrUpdateSensorRequest registration && registration.PreviousPaths == null)
            {
                var node = JsonSerializer.SerializeToNode(registration, registration.GetType(), options) as System.Text.Json.Nodes.JsonObject;

                if (node != null)
                {
                    node.Remove(nameof(HSMSensorDataObjects.SensorRequests.AddOrUpdateSensorRequest.PreviousPaths));
                    node.WriteTo(writer, options);
                    return;
                }
            }

            JsonSerializer.Serialize(writer, value, value.GetType(), options);
        }
    }
}
