using HSMDatabase.DatabaseInterface;
using HSMDatabase.DatabaseWorkCore;
using HSMServer.Authentication;
using HSMServer.DataLayer;
using HSMServer.DataLayer.Model;
using HSMServer.Keys;
using System;
using System.Collections.Generic;
using HSMDatabase.Entity;
using HSMSensorDataObjects;
using HSMServer.Configuration;
using HSMServer.Registration;

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
            IPublicAdapter publicAdapter = new PublicAdapter();
            IDatabaseCore core = DatabaseCore.GetInstance();
            DatabaseAdapter = new DatabaseAdapter(publicAdapter, core);
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


        public SensorInfo CreateOneValueSensorInfo()
        {
            return CreateSensorInfo(OneValueSensorPath, "OneValue", FirstProductName);
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
            entity.Time = DateTime.Now;
            entity.TimeCollected = DateTime.Now.AddDays(-1 * days);
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
            DatabaseAdapter?.RemoveProductOld(FirstProductName);
            DatabaseAdapter?.RemoveProductOld(SecondProductName);
            DatabaseAdapter?.RemoveProductOld(ThirdProductName);
            DatabaseAdapter?.RemoveUserOld(CreateFirstUser());
            DatabaseAdapter?.RemoveUserOld(CreateSecondUser());
            DatabaseAdapter?.RemoveUserOld(CreateThirdUser());
            DatabaseAdapter?.RemoveRegistrationTicketOld(_ticket.Id);
            DatabaseAdapter?.RemoveConfigurationObjectOld(ConfigurationObjectName);
        }
    }
}
