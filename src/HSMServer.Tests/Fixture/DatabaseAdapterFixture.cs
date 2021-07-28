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
            DatabaseAdapter = new DatabaseAdapter(publicAdapter);
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
            return CreateDataEntity(SensorPath, "Test");
        }

        public SensorDataEntity CreateOneValueSensorDataEntity()
        {
            return CreateDataEntity(OneValueSensorPath, "File bytes");
        }

        public List<SensorDataEntity> CreateSensorValues()
        {
            List<SensorDataEntity> result = new List<SensorDataEntity>();
            for (int i = 0; i < 20; i++)
            {
                result.Add(CreateDataEntity(SensorPath, i.ToString()));
            }

            return result;
        }
        private SensorDataEntity CreateDataEntity(string path, string value)
        {
            SensorDataEntity entity = new SensorDataEntity();
            entity.Path = path;
            entity.Time = DateTime.Now;
            entity.TimeCollected = DateTime.Now;
            entity.Status = (byte) SensorStatus.Ok;
            entity.DataType = (byte) SensorType.IntSensor;
            entity.TypedData = value;
            entity.Timestamp = DateTime.Now.Ticks;
            return entity;
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
        }
    }
}
