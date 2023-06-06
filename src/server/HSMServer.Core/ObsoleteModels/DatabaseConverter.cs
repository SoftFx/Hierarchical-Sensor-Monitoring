using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Registration;

namespace HSMServer.Core.Converters
{
    public static class DatabaseConverter
    {
        public static RegisterTicketEntity ConvertToEntity(this RegistrationTicket ticket) =>
            new()
            {
                Role = ticket.Role,
                ExpirationDate = ticket.ExpirationDate,
                Id = ticket.Id,
                ProductKey = ticket.ProductKey,
            };
    }
}
