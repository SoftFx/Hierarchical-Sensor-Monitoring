namespace HSMCommon.Keys
{
    public static class SensorKeyGenerator
    {
        public static string GenerateKey(string serverName, string sensorName)
        {
            return HashComputer.ComputeSha256Hash($"{serverName}_{sensorName}");
        }
    }
}
