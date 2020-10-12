using System;
using System.Text;

namespace HSMCommon.Keys
{
    public static class SensorKeyGenerator
    {
        public static string GenerateKey(string serverName, string sensorName)
        {
            //return HashComputer.ComputeSha256Hash($"{serverName}_{sensorName}");
            return Convert.ToBase64String(Encoding.ASCII.GetBytes($"{serverName}_{sensorName}"));
        }
    }
}
