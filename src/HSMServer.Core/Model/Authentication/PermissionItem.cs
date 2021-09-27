using System.Collections.Generic;

namespace HSMServer.Core.Model.Authentication
{
    public class PermissionItem
    {
        public string ProductName { get; set; }
        public List<string> IgnoredSensors { get; set; }
    }
}
