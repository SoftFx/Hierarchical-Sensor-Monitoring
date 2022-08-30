using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Authentication;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.Converters
{
    public static class DatabaseConverter
    {
        public static UserEntity ConvertToEntity(this User user) =>
            new()
            {
                UserName = user.UserName,
                Password = user.Password,
                CertificateThumbprint = user.CertificateThumbprint,
                CertificateFileName = user.CertificateFileName,
                Id = user.Id,
                IsAdmin = user.IsAdmin,
                ProductsRoles = user.ProductsRoles?.Select(r => new KeyValuePair<string, byte>(r.Key, (byte)r.Value))?.ToList(),
                NotificationSettings = user.Notifications.ToEntity(),
                Filter = user.Filter.ToEntity(),
            };

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
