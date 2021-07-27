using HSMDatabase.DatabaseInterface;
using HSMDatabase.DatabaseWorkCore;
using HSMServer.DataLayer;
using HSMServer.DataLayer.Model;
using HSMServer.Keys;
using System;
using System.Collections.Generic;

namespace HSMServer.Tests.Fixture
{
    public class DatabaseAdapterFixture : IDisposable
    {
        public IDatabaseAdapter DatabaseAdapter { get; }
        public DatabaseAdapterFixture()
        {
            IPublicAdapter publicAdapter = new PublicAdapter();
            DatabaseAdapter = new DatabaseAdapter(publicAdapter);
        }

        private void CreateConstants()
        {

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

        public readonly string FirstUserName = $"First_user_name_{DateTime.Now.Ticks}";
        public readonly string SecondUserName = $"Second_user_name_{DateTime.Now.Ticks}";
        public readonly string ThirdUserName = $"Third_user_name_{DateTime.Now.Ticks}";

        public void Dispose()
        {
            DatabaseAdapter?.RemoveProduct(FirstProductName);
            DatabaseAdapter?.RemoveProduct(SecondProductName);
            DatabaseAdapter?.RemoveProduct(ThirdProductName);
        }
    }
}
