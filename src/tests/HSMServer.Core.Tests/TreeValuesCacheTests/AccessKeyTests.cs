using HSMServer.Core.Cache;
using HSMServer.Core.Cache.Entities;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using HSMServer.Core.Tests.TreeValuesCacheTests.Fixture;
using System;
using System.Collections.Generic;
using Xunit;

namespace HSMServer.Core.Tests.TreeValuesCacheTests
{
    public class AccessKeyTests : MonitoringCoreTestsBase<AccessKeyFixture>, IDisposable
    {
        private readonly ITreeValuesCache _valuesCache;

        private (int add, int update, int delete) _productTransactionCount;
        private (int add, int update, int delete) _keyTransactionCount;

        private const int DefaultKeyCount = 1;
        private const int ProductAddTransactionCount = 1; 


        public AccessKeyTests(AccessKeyFixture fixture, DatabaseRegisterFixture dbFixture)
            : base(fixture, dbFixture, addTestProduct: true)
        {
            _valuesCache = new TreeValuesCache(_databaseCoreManager.DatabaseCore, _userManager);

            _productTransactionCount = (0, 0, 0);
            _keyTransactionCount = (0, 0, 0);

            _valuesCache.ChangeProductEvent += ProductEventHandler;
            _valuesCache.ChangeAccessKeyEvent += KeyEventHandler;
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
            var product = _valuesCache.AddProduct(RandomGenerator.GetRandomString());

            var keys = new List<AccessKeyModel>(count);
            for (int i = 0; i < count; i++)
            {
                var model = new AccessKeyModel(EntitiesFactory.BuildAccessKeyEntity(productId: product.Id));

                keys.Add(model);
                _valuesCache.AddAccessKey(model);
            }

            CheckTransactionsCount((add: ProductAddTransactionCount, 
                update: count + DefaultKeyCount, delete: 0), _productTransactionCount);

            CheckTransactionsCount((add: count + DefaultKeyCount, update: 0, delete: 0), _keyTransactionCount);

            ModelsTester.TestProductModel(product, _valuesCache.GetProduct(product.Id));

            foreach (var expected in keys)
                ModelsTester.TestAccessKeyModel(expected, _valuesCache.GetAccessKey(expected.Id));
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
            var product = _valuesCache.AddProduct(RandomGenerator.GetRandomString());

            var keyIds = new List<Guid>(count);
            for (int i = 0; i < count; i++)
                keyIds.Add(_valuesCache.AddAccessKey(new AccessKeyModel
                    (EntitiesFactory.BuildAccessKeyEntity(productId: product.Id))).Id);

            foreach (var id in keyIds)
                _valuesCache.RemoveAccessKey(id);

            // 2 * count = count (added keys) + count (removed keys)
            CheckTransactionsCount((add: ProductAddTransactionCount,
                update: (2 * count) + DefaultKeyCount, delete: 0), _productTransactionCount);

            CheckTransactionsCount((add: count + DefaultKeyCount, update: 0, delete: count), _keyTransactionCount);

            ModelsTester.TestProductModel(product, _valuesCache.GetProduct(product.Id));

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

            var product = _valuesCache.AddProduct(RandomGenerator.GetRandomString());

            var updates = new List<AccessKeyUpdate>(count);
            for (int i = 0; i < count; i++)
            {
                var id = _valuesCache.AddAccessKey(new AccessKeyModel
                    (EntitiesFactory.BuildAccessKeyEntity(productId: product.Id))).Id;

                updates.Add(BuildKeyUpdate(id));
            }

            updates.ForEach(upd => _valuesCache.UpdateAccessKey(upd));

            CheckTransactionsCount((add: ProductAddTransactionCount,
                update: count + DefaultKeyCount, delete: 0), _productTransactionCount);

            CheckTransactionsCount((add: count + DefaultKeyCount, update: count, delete: 0),
                _keyTransactionCount);

            ModelsTester.TestProductModel(product, _valuesCache.GetProduct(product.Id));

            foreach (var update in updates)
                ModelsTester.TestAccessKeyModel(update, _valuesCache.GetAccessKey(update.Id));
        }

        //getAll


        public void Dispose()
        {
            _valuesCache.ChangeAccessKeyEvent -= KeyEventHandler;
            _valuesCache.ChangeProductEvent -= ProductEventHandler;
        }


        private void ProductEventHandler(ProductModel model, TransactionType type)
        {
            Assert.NotNull(model);

            CheckTransaction(type, ref _productTransactionCount);
        }

        private void KeyEventHandler(AccessKeyModel model, TransactionType type)
        {
            Assert.NotNull(model);

            CheckTransaction(type, ref _keyTransactionCount);
        }

        private static void CheckTransaction(TransactionType type, 
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

        private static void CheckTransactionsCount((int add, int update, int delete) expected,
            (int add, int update, int delete) actual)
        {
            Assert.Equal(expected.add, actual.add);
            Assert.Equal(expected.update, actual.update);
            Assert.Equal(expected.delete, actual.delete);
        }
    }
}
