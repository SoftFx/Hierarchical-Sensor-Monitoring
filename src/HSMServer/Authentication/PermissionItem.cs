using System.Collections.Generic;

namespace HSMServer.Authentication
{
    internal class PermissionItem
    {
        public string ProductName { get; set; }
        public List<string> IgnoredSensors { get; set; }
    }
}
