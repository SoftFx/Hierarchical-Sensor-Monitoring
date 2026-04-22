using System;

namespace HSMServer.Controllers
{
    public class CreateMcpKeyRequest
    {
        public string DisplayName { get; set; }
        public DateTime? ExpirationTime { get; set; }
    }
}