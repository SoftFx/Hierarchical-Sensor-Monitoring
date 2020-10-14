using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HSMServer.Authentication
{
    public class PermissionItem
    {
        public string ProductName { get; set; }
        public List<string> IgnoredSensors { get; set; }
    }
}
