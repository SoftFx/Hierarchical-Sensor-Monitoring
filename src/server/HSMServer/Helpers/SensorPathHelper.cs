using System;

namespace HSMServer.Helpers
{
    public static class SensorPathHelper
    {
        public static string EncodeGuid(Guid id) => id.ToString();

        public static Guid DecodeGuid(string id) => Guid.TryParse(id, out var guid) ? guid : Guid.Empty;
    }
}
