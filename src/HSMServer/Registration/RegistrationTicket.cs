using System;

namespace HSMServer.Registration
{
    public class RegistrationTicket
    {
        public Guid Id { get; set; }
        public string ProductKey { get; set; }
        public string Role { get; set; }
        public DateTime ExpirationDate { get; set; }
    }
}
