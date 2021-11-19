using HSMCommon;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMSensorDataObjects;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Keys;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Authentication;
using HSMServer.Core.Model.Sensor;
using System;
using System.Collections.Generic;

namespace HSMServer.Tests.Fixture
{
    public class DatabaseAdapterFixture : IDisposable
    {
        public IDatabaseAdapter DatabaseAdapter { get; }
        /// <summary>
        /// Use as Set Up method for all Database tests
        /// </summary>
        public DatabaseAdapterFixture()
        {
            DatabaseAdapter = new DatabaseAdapter();
        }
        
        #region Product

        public readonly string FirstProductName = "First_product_name";
        public readonly string SecondProductName = "Second_product_name";
        public readonly string ThirdProductName = "Third_product_name";
        public readonly string ExtraKeyName = "Extra_key";
        
        public Product GetFirstTestProduct()
        {
            return CreateProduct(FirstProductName, KeyGenerator.GenerateProductKey(FirstProductName));
        }

        public Product GetSecondTestProduct()
        {
            return CreateProduct(SecondProductName, KeyGenerator.GenerateProductKey(SecondProductName));
        }

        public Product GetThirdTestProduct()
        {
            return CreateProduct(ThirdProductName, KeyGenerator.GenerateProductKey(ThirdProductName));
        }

        public List<Product> GetProductsList()
        {
            return new List<Product>() {GetFirstTestProduct(), GetSecondTestProduct(), GetThirdTestProduct()};
        }
        private Product CreateProduct(string name, string key)
        {
            Product product = new Product();
            product.Name = name;
            product.DateAdded = DateTime.Now;
            product.Key = key;
            product.ExtraKeys = new List<ExtraProductKey>();
            return product;
        }

        #endregion

        #region Users

        public readonly string FirstUserName = "First_user_name";
        public readonly string SecondUserName = "Second_user_name";
        public readonly string ThirdUserName = "Third_user_name";

        public User CreateFirstUser()
        {
            return CreateUser(FirstUserName, HashComputer.ComputePasswordHash(FirstUserName));
        }

        public User CreateSecondUser()
        {
            return CreateUser(SecondUserName, HashComputer.ComputePasswordHash(SecondUserName));
        }

        public User CreateThirdUser()
        {
            return CreateUser(ThirdUserName, HashComputer.ComputePasswordHash(ThirdUserName));
        }
        private User CreateUser(string userName, string password)
        {
            User user = new User();
            user.UserName = userName;
            user.Password = password;
            user.IsAdmin = false;
            return user;
        }
        #endregion

        #region Sensors

        public const string OneValueSensorPath = "sensors/OneValue";
        public const string SensorPath = "sensors/Sensor";
        public const string SensorPath2 = "sensors/Sensor2";


        public SensorInfo CreateOneValueSensorInfo()
        {
            return CreateSensorInfo(OneValueSensorPath, "OneValue", FirstProductName);
        }

        public SensorInfo CreateSensorInfo2()
        {
            return CreateSensorInfo(SensorPath2, "Sensor2", FirstProductName);
        }
        public SensorInfo CreateSensorInfo()
        {
            return CreateSensorInfo(SensorPath, "Sensor", FirstProductName);
        }
        private SensorInfo CreateSensorInfo(string path, string name, string product)
        {
            SensorInfo info = new SensorInfo();
            info.Description = "Description";
            info.ProductName = product;
            info.SensorName = name;
            info.Path = path;
            return info;
        }

        public SensorDataEntity CreateOneDataEntity()
        {
            return CreateDataEntity(SensorPath, "Test", 0);
        }

        public SensorDataEntity CreateOneValueSensorDataEntity()
        {
            return CreateDataEntity(OneValueSensorPath, "File bytes", 0);
        }

        public List<SensorDataEntity> CreateSensorValues2()
        {
            List<SensorDataEntity> result = new List<SensorDataEntity>();
            for (int i = 0; i < 20; i++)
            {
                result.Add(CreateDataEntity(SensorPath2, i.ToString(), i));
            }

            return result;
        }
        public List<SensorDataEntity> CreateSensorValues()
        {
            List<SensorDataEntity> result = new List<SensorDataEntity>();
            for (int i = 0; i < 20; i++)
            {
                result.Add(CreateDataEntity(SensorPath, i.ToString(), i));
            }

            return result;
        }
        private SensorDataEntity CreateDataEntity(string path, string value, int days)
        {
            SensorDataEntity entity = new SensorDataEntity();
            entity.Path = path;
            entity.TimeCollected = DateTime.Now.AddDays(-1 * days);
            entity.Time = entity.TimeCollected;
            entity.Status = (byte) SensorStatus.Ok;
            entity.DataType = (byte) SensorType.IntSensor;
            entity.TypedData = value;
            entity.Timestamp = DateTime.Now.Ticks;
            return entity;
        }
        #endregion

        #region Tickets

        private RegistrationTicket _ticket = new RegistrationTicket()
        {
            Role = ProductRoleEnum.ProductManager.ToString(),
            ExpirationDate = DateTime.Now.AddMinutes(30),
            ProductKey = KeyGenerator.GenerateProductKey("Name for testing"),
        };
        public RegistrationTicket CreateRegistrationTicket()
        {
            return _ticket;
        }
        
        #endregion

        #region ConfigurationObject

        public const string ConfigurationObjectName = "Config";
        public const string ConfigurationObjectValue = "123";
        public ConfigurationObject CreateConfigurationObject()
        {
            ConfigurationObject result = new ConfigurationObject();
            result.Name = ConfigurationObjectName;
            result.Value = ConfigurationObjectValue;
            return result;
        }

        #endregion
        /// <summary>
        /// Use as Tear Down method for Database tests
        /// </summary>
        public void Dispose()
        {
            DatabaseAdapter?.RemoveProduct(FirstProductName);
            DatabaseAdapter?.RemoveProduct(SecondProductName);
            DatabaseAdapter?.RemoveProduct(ThirdProductName);
            DatabaseAdapter?.RemoveUser(CreateFirstUser());
            DatabaseAdapter?.RemoveUser(CreateSecondUser());
            DatabaseAdapter?.RemoveUser(CreateThirdUser());
            DatabaseAdapter?.RemoveRegistrationTicket(_ticket.Id);
            DatabaseAdapter?.RemoveConfigurationObject(ConfigurationObjectName);
        }
    }
}
