using System;
using System.Text.Json.Serialization;

namespace HSMSensorDataObjects
{
    public abstract class BaseRequest
    {
        [JsonIgnore]
        [Obsolete("Send key in request header instead")]
        public string Key { get; set; }

        public string Path { get; set; }
    }
}
