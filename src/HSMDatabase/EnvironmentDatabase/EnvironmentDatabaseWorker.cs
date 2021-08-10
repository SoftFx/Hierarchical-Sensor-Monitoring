using System;
using System.Collections.Generic;
using HSMDatabase.Entity;

namespace HSMDatabase.EnvironmentDatabase
{
    internal class EnvironmentDatabaseWorker : IEnvironmentDatabase
    {
        public EnvironmentDatabaseWorker(string name)
        {

        }

        public void AddProductToList(string productName)
        {
            throw new NotImplementedException();
        }

        public List<string> GetProductsList()
        {
            throw new NotImplementedException();
        }

        public ProductEntity GetProductInfo(string productName)
        {
            throw new NotImplementedException();
        }

        public void PutProductInfo(ProductEntity product)
        {
            throw new NotImplementedException();
        }

        public void RemoveProductInfo(string name)
        {
            throw new NotImplementedException();
        }

        public void RemoveProductFromList(string name)
        {
            throw new NotImplementedException();
        }

        public void RemoveSensor(string productName, string path)
        {
            throw new NotImplementedException();
        }

        public void AddSensor(SensorEntity info)
        {
            throw new NotImplementedException();
        }

        public void WriteSensorData(SensorDataEntity dataObject, string productName)
        {
            throw new NotImplementedException();
        }

        public SensorDataEntity GetOneValueSensorValue(string productName, string path)
        {
            throw new NotImplementedException();
        }

        public SensorDataEntity GetLatestSensorValue(string productName, string path)
        {
            throw new NotImplementedException();
        }

        public List<SensorDataEntity> GetSensorDataHistory(string productName, string path, long n)
        {
            throw new NotImplementedException();
        }

        public List<string> GetSensorsList(string productName)
        {
            throw new NotImplementedException();
        }

        public void AddNewSensorToList(string productName, string path)
        {
            throw new NotImplementedException();
        }

        public void RemoveSensorsList(string productName)
        {
            throw new NotImplementedException();
        }

        public void RemoveSensorFromList(string productName, string sensorName)
        {
            throw new NotImplementedException();
        }

        public SensorEntity GetSensorInfo(string productName, string path)
        {
            throw new NotImplementedException();
        }

        public void RemoveSensorValues(string productName, string path)
        {
            throw new NotImplementedException();
        }

        public void AddUser(UserEntity user)
        {
            throw new NotImplementedException();
        }

        public List<UserEntity> ReadUsers()
        {
            throw new NotImplementedException();
        }

        public void RemoveUser(UserEntity user)
        {
            throw new NotImplementedException();
        }

        public List<UserEntity> ReadUsersPage(int page, int pageSize)
        {
            throw new NotImplementedException();
        }

        public ConfigurationEntity ReadConfigurationObject(string name)
        {
            throw new NotImplementedException();
        }

        public void WriteConfigurationObject(ConfigurationEntity obj)
        {
            throw new NotImplementedException();
        }

        public void RemoveConfigurationObject(string name)
        {
            throw new NotImplementedException();
        }

        public RegisterTicketEntity ReadRegistrationTicket(Guid id)
        {
            throw new NotImplementedException();
        }

        public void RemoveRegistrationTicket(Guid id)
        {
            throw new NotImplementedException();
        }

        public void WriteRegistrationTicket(RegisterTicketEntity ticket)
        {
            throw new NotImplementedException();
        }
    }
}