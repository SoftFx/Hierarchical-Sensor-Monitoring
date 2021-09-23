namespace HSM.Core.Monitoring
{
    internal class MonitoringConstants
    {
        public const string RequestSizeSensorPath = "Load/Received data per second KB";
        public const string SensorsCountSensorPath = "Load/Received sensors per second";
        public const string RequestsCountSensorPath = "Load/Requests per second";
        public const string ResponseSizeSensorPath = "Load/Sent data per second KB";

        public const string DatabaseSizePath = "Database/All database size MB";
        public const string MonitoringDataSizePath = "Database/Monitoring data size MB";
        public const string EnvironmentDataSizePath = "Database/Environment data size MB";
    }
}