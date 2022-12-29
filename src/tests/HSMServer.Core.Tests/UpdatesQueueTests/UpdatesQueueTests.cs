using HSMServer.Core.Model;
using HSMServer.Core.SensorsUpdatesQueue;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace HSMServer.Core.Tests.UpdatesQueueTests
{
    public class UpdatesQueueTests
    {
        private const int DelayTime = 100;

        [Fact]
        [Trait("Category", "Add Item(s)")]
        public async Task AddItemTest()
        {
            StoreInfo receivedInfo = default;
            void GetItem(List<StoreInfo> items)
            {
                receivedInfo = items[0];
            }

            StoreInfo storeInfo = BuildStoreInfo(0);
            var updatesQueue = new UpdatesQueue();
            updatesQueue.AddItem(storeInfo);
            updatesQueue.NewItemsEvent += GetItem;

            await Task.Delay(DelayTime);
            Assert.Equal(storeInfo, receivedInfo);
        }

        [Fact]
        [Trait("Category", "Add Item(s)")]
        public async Task AddEmptyItemTest()
        {
            StoreInfo receivedInfo = default;
            void GetItem(List<StoreInfo> items)
            {
                receivedInfo = items[0];
            }

            StoreInfo storeInfo = new("", "/");
            var updatesQueue = new UpdatesQueue();
            updatesQueue.AddItem(storeInfo);
            updatesQueue.NewItemsEvent += GetItem;

            await Task.Delay(DelayTime);
            Assert.Equal(storeInfo, receivedInfo);
        }

        [Theory]
        [InlineData(5)]
        [InlineData(10)]
        [InlineData(100)]
        [Trait("Category", "Add Item(s)")]
        public async Task AddItemsTest(int count)
        {
            List<StoreInfo> receivedInfo = new(count);
            void GetItem(List<StoreInfo> items)
            {
                receivedInfo.AddRange(items);
            }

            List<StoreInfo> items = AddItemsToList(count);

            var updatesQueue = new UpdatesQueue();
            updatesQueue.AddItems(items);
            updatesQueue.NewItemsEvent += GetItem;

            await Task.Delay(DelayTime);
            Assert.Equal(items.Count, receivedInfo.Count);
            for (int i = 0; i < items.Count; i++)
               Assert.Equal(items[i], receivedInfo[i]);
        }

        [Theory]
        [InlineData(5)]
        [InlineData(10)]
        [InlineData(100)]
        [Trait("Category", "Add Item(s)")]
        public async Task AddEmptyItemsTest(int count)
        {
            List<StoreInfo> receivedInfo = new(count);
            void GetItem(List<StoreInfo> storeInfo)
            {
                receivedInfo.AddRange(storeInfo);
            }

            List<StoreInfo> items = new(count);
            for (int i = 0; i < count; i++)
            {
                StoreInfo storeInfo = new("", "/");
                items.Add(storeInfo);
            }

            var updatesQueue = new UpdatesQueue();
            updatesQueue.AddItems(items);
            updatesQueue.NewItemsEvent += GetItem;

            await Task.Delay(DelayTime);
            Assert.Equal(items.Count, receivedInfo.Count);
            for (int i = 0; i < items.Count; i++)
                Assert.Equal(items[i], receivedInfo[i]);
        }

        private static List<StoreInfo> AddItemsToList(int count)
        {
            List<StoreInfo> items = new(count);

            for (int i = 0; i < count; i++)
            {
                StoreInfo storeInfo = BuildStoreInfo(i);
                items.Add(storeInfo);
            }

            return items;
        }

        private static StoreInfo BuildStoreInfo(int value)
        {
            return new("", "/") { BaseValue = new IntegerValue() { Value = value } };
        }
    }
}
