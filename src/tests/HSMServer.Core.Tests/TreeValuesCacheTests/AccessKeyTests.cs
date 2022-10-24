using HSMServer.Core.Cache;
using HSMServer.Core.Cache.UpdateEntitites;
using HSMServer.Core.Model;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using HSMServer.Core.Tests.TreeValuesCacheTests.Fixture;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace HSMServer.Core.Tests.TreeValuesCacheTests
{
    public class AccessKeyTests : MonitoringCoreTestsBase<AccessKeyFixture>, IDisposable
    {
        private const int DefaultKeyCount = 1;
        private const int ProductAddTransactionCount = 1;

        private readonly ITreeValuesCache _valuesCache;

        private (int add, int update, int delete) _productTransactionCount;
        private (int add, int update, int delete) _keyTransactionCount;
        private ProductModel _product;

        private delegate ProductModel GetProduct(string id);
        private delegate AccessKeyModel GetAccessKey(Guid id);


        public AccessKeyTests(AccessKeyFixture fixture, DatabaseRegisterFixture dbFixture)
            : base(fixture, dbFixture, addTestProduct: true)
        {
            _valuesCache = new TreeValuesCache(_databaseCoreManager.DatabaseCore, _userManager, _updatesQueue);

            _productTransactionCount = (0, 0, 0);
            _keyTransactionCount = (0, 0, 0);

            _valuesCache.ChangeProductEvent += EventHandler;
            _valuesCache.ChangeAccessKeyEvent += EventHandler;

            _product = _valuesCache.AddProduct(RandomGenerator.GetRandomString());
        }


        [Theory]
        [InlineData(1)]
        [InlineData(10)]
        [InlineData(50)]
        [InlineData(100)]
        [InlineData(1000)]
        [Trait("Cetagory", "Add access key(s)")]
        public void AddAccessKeysTest(int count)
        {
            List<AccessKeyModel> keys = AddRandomKeys(count);

            AssertTransactionsCount((add: ProductAddTransactionCount,
                update: count + DefaultKeyCount, delete: 0), _productTransactionCount);

            AssertTransactionsCount((add: count + DefaultKeyCount, update: 0, delete: 0),
                _keyTransactionCount);

            TestProductAndKeys(keys, _valuesCache.GetProduct, _valuesCache.GetAccessKey);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(10)]
        [InlineData(50)]
        [InlineData(100)]
        [InlineData(1000)]
        [Trait("Category", "Remove access key(s)")]
        public void RemoveAccessKeysTest(int count)
        {
            List<Guid> keyIds = AddRandomKeys(count).Select(k => k.Id).ToList();

            foreach (var id in keyIds)
                _valuesCache.RemoveAccessKey(id);

            // 2 * count = count (added keys) + count (removed keys)
            AssertTransactionsCount((add: ProductAddTransactionCount,
                update: (2 * count) + DefaultKeyCount, delete: 0), _productTransactionCount);

            AssertTransactionsCount((add: count + DefaultKeyCount, update: 0, delete: count),
                _keyTransactionCount);

            ModelsTester.TestProductModel(_product, _valuesCache.GetProduct(_product.Id));

            keyIds.ForEach(id => Assert.Null(_valuesCache.GetAccessKey(id)));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(10)]
        [InlineData(50)]
        [InlineData(100)]
        [InlineData(1000)]
        [Trait("Category", "Update access key(s)")]
        public void UpdateAccessKeysTest(int count)
        {
            static AccessKeyUpdate BuildKeyUpdate(Guid id) =>
               new()
               {
                   Id = id,
                   DisplayName = RandomGenerator.GetRandomString(),
                   Comment = RandomGenerator.GetRandomString(),
                   Permissions = KeyPermissions.CanSendSensorData | KeyPermissions.CanAddSensors,
                   State = KeyState.Blocked
               };

            var updatedKeys = new List<AccessKeyModel>(count);
            for (int i = 0; i < count; i++)
            {
                var id = _valuesCache.AddAccessKey(BuildAccessKeyModel()).Id;

                updatedKeys.Add(_valuesCache.UpdateAccessKey(BuildKeyUpdate(id)));
            }

            AssertTransactionsCount((add: ProductAddTransactionCount,
                update: count + DefaultKeyCount, delete: 0), _productTransactionCount);

            AssertTransactionsCount((add: count + DefaultKeyCount, update: count, delete: 0),
                _keyTransactionCount);

            TestProductAndKeys(updatedKeys, _valuesCache.GetProduct, _valuesCache.GetAccessKey);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(10)]
        [InlineData(50)]
        [InlineData(100)]
        [InlineData(1000)]
        [Trait("Category", "GetAll access key(s)")]
        public void GetAllAccessKeysTest(int count)
        {
            var keys = new List<AccessKeyModel>(count + 1);

            keys.AddRange(_product.AccessKeys.Values);
            keys.AddRange(AddRandomKeys(count));

            AssertTransactionsCount((add: ProductAddTransactionCount,
                update: count + DefaultKeyCount, delete: 0), _productTransactionCount);

            AssertTransactionsCount((add: count + DefaultKeyCount, update: 0, delete: 0),
                _keyTransactionCount);

            TestProductAndKeys(keys, _valuesCache.GetProduct, _valuesCache.GetAccessKey);
        }


        public void Dispose()
        {
            _valuesCache.ChangeAccessKeyEvent -= EventHandler;
            _valuesCache.ChangeProductEvent -= EventHandler;
            _product = null;
        }

        private void EventHandler<T>(T model, TransactionType type)
        {
            static void CheckTransaction(TransactionType type,
            ref (int add, int update, int delete) transactionCount)
            {
                switch (type)
                {
                    case TransactionType.Add:
                        transactionCount.add++;
                        break;

                    case TransactionType.Update:
                        transactionCount.update++;
                        break;

                    case TransactionType.Delete:
                        transactionCount.delete++;
                        break;
                }
            }

            Assert.NotNull(model);

            if (model is ProductModel)
                CheckTransaction(type, ref _productTransactionCount);

            else if (model is AccessKeyModel)
                CheckTransaction(type, ref _keyTransactionCount);
        }

        private static void AssertTransactionsCount((int add, int update, int delete) expected,
            (int add, int update, int delete) actual) => Assert.True(expected == actual);

        private void TestProductAndKeys(List<AccessKeyModel> keys, GetProduct getProduct,
            GetAccessKey getKey)
        {
            ModelsTester.TestProductModel(_product, getProduct?.Invoke(_product.Id));

            foreach (var key in keys)
                ModelsTester.TestAccessKeyModel(key, getKey?.Invoke(key.Id));
        }

        private List<AccessKeyModel> AddRandomKeys(int count)
        {
            var keys = new List<AccessKeyModel>(count);

            for (int i = 0; i < count; i++)
                keys.Add(_valuesCache.AddAccessKey(BuildAccessKeyModel()));

            return keys;
        }

        private AccessKeyModel BuildAccessKeyModel() => new(EntitiesFactory
            .BuildAccessKeyEntity(productId: _product.Id));
    }
}
