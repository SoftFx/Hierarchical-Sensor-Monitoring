using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model;

namespace HSMServer.Core.Converters
{
    public static class DatabaseConverter
    {
        public static ConfigurationEntity ConvertToEntity(this ConfigurationObject obj) =>
            new()
            {
                Value = obj.Value,
                Name = obj.Name,
            };

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
