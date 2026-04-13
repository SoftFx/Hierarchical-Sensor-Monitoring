namespace HSMServer.Controllers.MCP
{
    public class UpdateScheduleInput
    {
        public string scheduleId { get; set; }
        public string name { get; set; }
        public string timezone { get; set; }
        public string schedule { get; set; }
    }
}
