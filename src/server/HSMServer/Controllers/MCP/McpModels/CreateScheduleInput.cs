namespace HSMServer.Controllers.MCP
{
    public class CreateScheduleInput
    {
        public string name { get; set; }
        public string timezone { get; set; } = "UTC";
        public string schedule { get; set; }
    }
}
