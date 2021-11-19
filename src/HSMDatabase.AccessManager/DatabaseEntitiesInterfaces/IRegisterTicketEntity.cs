using System;

namespace HSMDatabase.AccessManager.DatabaseEntities
{
    public interface IRegisterTicketEntity
    {
        public Guid Id { get; set; }
        public string ProductKey { get; set; }
        public string Role { get; set; }
        public DateTime ExpirationDate { get; set; }
    }
}
