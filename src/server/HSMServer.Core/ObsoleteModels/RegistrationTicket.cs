using System;
using HSMDatabase.AccessManager.DatabaseEntities;

namespace HSMServer.Core.Registration
{
    public class RegistrationTicket
    {
        public Guid Id { get; }
        public string ProductKey { get; set; }
        public string Role { get; set; }
        public DateTime ExpirationDate { get; set; }

        public RegistrationTicket()
        {
            Id = Guid.NewGuid();
        }
        public RegistrationTicket(RegisterTicketEntity entity)
        {
            Id = entity.Id;
            ProductKey = entity.ProductKey;
            Role = entity.Role;
            ExpirationDate = entity.ExpirationDate;
        }
    }
}
