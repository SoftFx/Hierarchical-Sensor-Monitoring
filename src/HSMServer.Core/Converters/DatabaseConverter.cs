using HSMDatabase.AccessManager.DatabaseEntities;
using HSMSensorDataObjects;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Authentication;
using HSMServer.Core.Model.Sensor;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.Converters
{
    public static class DatabaseConverter
    {
        public static SensorHistoryData ConvertToHistoryData(this SensorDataEntity entity) =>
        new()
        {
            SensorType = (SensorType)entity.DataType,
            TypedData = entity.TypedData,
            Time = entity.Time,
            OriginalFileSensorContentSize = entity.OriginalFileSensorContentSize,
        };

        public static ProductEntity ConvertToEntity(this Product product) =>
            new()
            {
                Name = product.Name,
                Key = product.Key,
                DateAdded = product.DateAdded,
                ExtraKeys = product.ExtraKeys?.Select(p => p.ConvertToEntity())?.ToList(),
            };

        public static ExtraKeyEntity ConvertToEntity(this ExtraProductKey key) =>
            new()
            {
                Key = key.Key,
                Name = key.Name,
            };

        public static SensorEntity ConvertToEntity(this SensorInfo  info) =>
            new()
            {
                Description = info.Description,
                Path = info.Path,
                ProductName = info.ProductName,
                SensorName = info.SensorName,
                SensorType = (int)info.SensorType,
                Unit = info.Unit,
                ExpectedUpdateIntervalTicks = info.ExpectedUpdateInterval.Ticks,
                ValidationParameters = info.ValidationParameters?.Select(i => i.ConvertToEntity())?.ToList(),
            };

        public static ValidationParameterEntity ConvertToEntity(this SensorValidationParameter validationParameter) =>
            new()
            {
                ValidationValue = validationParameter.ValidationValue,
                ParameterType = (int)validationParameter.ValidationType
            };

        public static UserEntity ConvertToEntity(this User  user) =>
            new()
            {
                UserName = user.UserName,
                Password = user.Password,
                CertificateThumbprint = user.CertificateThumbprint,
                CertificateFileName = user.CertificateFileName,
                Id = user.Id,
                IsAdmin = user.IsAdmin,
                ProductsRoles = user.ProductsRoles?.Select(r => new KeyValuePair<string, byte>(r.Key, (byte)r.Value))?.ToList(),
            };

        public static ConfigurationEntity ConvertToEntity(this ConfigurationObject  obj) =>
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
