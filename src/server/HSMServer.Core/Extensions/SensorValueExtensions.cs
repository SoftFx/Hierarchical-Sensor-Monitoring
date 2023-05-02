using HSMServer.Core.Model;
using System;
using System.Text.Json;

namespace HSMServer.Core.Extensions
{
    public static class SensorValueExtensions
    {
        public static BaseValue ToValue<T>(this byte[] bytes) where T : BaseValue
        {
            if (bytes == null)
                return null;

            var rootElement = JsonDocument.Parse(bytes).RootElement;

            return rootElement.Deserialize<T>();
        }


        public static bool InRange<T>(this T value, DateTime from, DateTime to) where T : BaseValue
        {
            return value.ReceivingTime >= from && value.ReceivingTime <= to;
        }
    }
}