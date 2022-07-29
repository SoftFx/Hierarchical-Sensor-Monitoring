using System;
using System.Collections.Generic;
using System.Text.Json;

namespace HSMServer.Core.DataLayer
{
    internal static class JsonElementExtensions
    {
        internal static T GetProperty<T>(this JsonElement rootElement, string propertyName,
                                        Func<JsonElement, T> getPropertyAction, T defaultValue = default) =>
            rootElement.TryGetProperty(propertyName, out var property) ? getPropertyAction(property) : defaultValue;


        internal static bool ReadBool(this JsonElement element, string propertyName) =>
            element.TryGetProperty(propertyName, out var property) && property.GetBoolean();

        internal static byte ReadByte(this JsonElement element, string propertyName) =>
            element.TryGetProperty(propertyName, out var property) ? property.GetByte() : default;

        internal static int ReadInt(this JsonElement element, string propertyName) =>
            element.TryGetProperty(propertyName, out var property) ? property.GetInt32() : default;

        internal static long ReadLong(this JsonElement element, string propertyName) =>
            element.TryGetProperty(propertyName, out var property) ? property.GetInt64() : default;

        internal static double ReadDouble(this JsonElement element, string propertyName) =>
            element.TryGetProperty(propertyName, out var property) ? property.GetDouble() : default;

        internal static string ReadString(this JsonElement element, string propertyName) =>
            element.TryGetProperty(propertyName, out var property) ? property.GetString() : default;

        internal static DateTime ReadDateTime(this JsonElement element, string propertyName) =>
            element.TryGetProperty(propertyName, out var property) ? property.GetDateTime() : default;

        internal static byte[] ReadBytes(this JsonElement element, string propertyName) =>
            element.TryGetProperty(propertyName, out var property) ? property.GetBytesFromBase64() : default;

        internal static Dictionary<double, T> ReadPercentiles<T>(this JsonElement element,
            string propertyName, Func<JsonElement, T> readPercentileValue)
        {
            var percentiles = new Dictionary<double, T>(1 << 2);

            if (element.TryGetProperty(propertyName, out var property))
            {
                foreach (var percentile in property.EnumerateArray())
                {
                    var key = percentile.ReadDouble(SensorValuesFactory.PercentileKeyPropertyName);
                    var value = readPercentileValue(percentile);

                    percentiles.Add(key, value);
                }
            }

            return percentiles;
        }
    }
}
