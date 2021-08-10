using System;
using System.Collections.Generic;
using HSMCommon.Model.SensorsData;
using HSMDatabase.Entity;
using HSMServer.Authentication;
using HSMServer.Configuration;
using HSMServer.DataLayer.Model;
using HSMServer.Registration;

namespace HSMServer.DataLayer
{
    public interface IDatabaseAdapter
    {
        #region Product Old
        [Obsolete]
        void RemoveProductOld(string productName);
        [Obsolete]
        void AddProductOld(Product product);
        [Obsolete]
        void UpdateProductOld(Product product);
        [Obsolete]
        Product GetProductOld(string productName);
        [Obsolete]
        List<Product> GetProductsOld();

        #endregion

        #region Sensors Old

        [Obsolete]
        void RemoveSensorOld(string productName, string path);
        [Obsolete]
        void AddSensorOld(SensorInfo info);
        [Obsolete]
        void UpdateSensorOld(SensorInfo info);
        [Obsolete]
        void PutSensorDataOld(SensorDataEntity data, string productName);
        [Obsolete]
        void PutOneValueSensorDataOld(SensorDataEntity data, string productName);
        [Obsolete]
        SensorDataEntity GetLastSensorValueOld(string productName, string path);
        [Obsolete]
        SensorInfo GetSensorInfoOld(string productName, string path);
        [Obsolete]
        List<SensorHistoryData> GetSensorHistoryOld(string productName, string path, long n);
        [Obsolete]
        SensorHistoryData GetOneValueSensorValueOld(string productName, string path);
        [Obsolete]
        List<SensorInfo> GetProductSensorsOld(Product product);
        #endregion

        #region User Old

        [Obsolete]
        void AddUserOld(User user);
        [Obsolete]
        void UpdateUserOld(User user);
        [Obsolete]
        void RemoveUserOld(User user);
        [Obsolete]
        List<User> GetUsersOld();
        [Obsolete]
        List<User> GetUsersPageOld(int page, int pageSize);

        #endregion

        #region Configuration Old
        [Obsolete]
        ConfigurationObject GetConfigurationObjectOld(string name);
        [Obsolete]
        void WriteConfigurationObjectOld(ConfigurationObject obj);
        [Obsolete]
        void RemoveConfigurationObjectOld(string name);

        #endregion

        #region Registration Ticket Old
        [Obsolete]
        RegistrationTicket ReadRegistrationTicketOld(Guid id);
        [Obsolete]
        void RemoveRegistrationTicketOld(Guid id);
        [Obsolete]
        void WriteRegistrationTicketOld(RegistrationTicket ticket);

        #endregion
    }
}