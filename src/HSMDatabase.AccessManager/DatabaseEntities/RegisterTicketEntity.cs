using System;
using HSMDatabase.AccessManager.DatabaseEntities;

namespace HSMDatabase.Entity
{
    public class RegisterTicketEntity : IRegisterTicketEntity
    {
        public Guid Id { get; set; }
        public string ProductKey { get; set; }
        public string Role { get; set; }
        public DateTime ExpirationDate { get; set; }
    }
}
