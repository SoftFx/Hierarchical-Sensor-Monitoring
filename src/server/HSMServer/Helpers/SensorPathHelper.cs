﻿using System;
using System.Text;

namespace HSMServer.Helpers
{
    public static class SensorPathHelper
    {
        public static string EncodeGuid(Guid id)
        {
            return (Convert.ToBase64String(Encoding.Unicode.GetBytes(id.ToString())))
                .Replace('=', '_').Replace('+', '-').Replace(':', '/');
        }

        public static Guid DecodeGuid(string id)
        {
            id = id.Replace('/', ':').Replace('-', '+').Replace('_', '=');
            return Guid.Parse(Encoding.Unicode.GetString(Convert.FromBase64String(id)));
        }
    }
}
