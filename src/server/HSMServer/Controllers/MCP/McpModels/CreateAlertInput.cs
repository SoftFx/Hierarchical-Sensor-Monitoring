namespace HSMServer.Controllers.MCP
{
    public class CreateAlertInput
    {
        public string sensorId { get; set; }
        public string condition { get; set; }
        public string property { get; set; } = "Value";
        public string targetValue { get; set; }
        public string combination { get; set; } = "and";
        public bool isEnabled { get; set; } = true;
        public string template { get; set; }
        public string icon { get; set; }
        public string triggerStatus { get; set; }
        public string confirmationPeriod { get; set; }
        public string repeatMode { get; set; }
        public bool instantSend { get; set; }
        public string scheduleTime { get; set; }
        public string destinationMode { get; set; }
        public string alertScheduleId { get; set; }
    }
}
