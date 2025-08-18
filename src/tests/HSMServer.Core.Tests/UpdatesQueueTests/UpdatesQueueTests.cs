using HSMServer.Core.Model;
using HSMServer.Core.Model.Requests;
using HSMServer.Core.SensorsUpdatesQueue;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace HSMServer.Core.Tests.UpdatesQueueTests
{
    public class UpdatesQueueTests
    {
        private const int DelayTime = 1000;

        private List<StoreInfo> _receivedInfos;

        private readonly ITestOutputHelper _output;

        public UpdatesQueueTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        [Trait("Category", "Add Item(s)")]
        public async Task AddItemTest()
        {
            StoreInfo receivedInfo = default;
            void GetItem(BaseRequestModel item)
            {
                receivedInfo = item as StoreInfo;
            }

            StoreInfo storeInfo = BuildStoreInfo(0);
            var updatesQueue = new UpdatesQueue();
            await updatesQueue.AddItemAsync(storeInfo);
            updatesQueue.ItemAdded += GetItem;

            await Task.Delay(DelayTime);
            Assert.Equal(storeInfo, receivedInfo);
        }

        [Fact]
        [Trait("Category", "Add Item(s)")]
        public async Task AddEmptyItemTest()
        {
            StoreInfo receivedInfo = default;
            void GetItem(BaseRequestModel item)
            {
                receivedInfo = item as StoreInfo;
            }

            StoreInfo storeInfo = new("", "/");
            var updatesQueue = new UpdatesQueue();
            await updatesQueue.AddItemAsync(storeInfo);
            updatesQueue.ItemAdded += GetItem;

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
            _receivedInfos = new(count);
            

            List<StoreInfo> items = AddItemsToList(count);

            var updatesQueue = new UpdatesQueue();
            updatesQueue.ItemAdded += GetItem;
            await updatesQueue.AddItemsAsync(items);



            await Task.Delay(DelayTime);

            updatesQueue.ItemAdded -= GetItem;
            updatesQueue.Dispose();

            Assert.Equal(items.Count, _receivedInfos.Count);
            for (int i = 0; i < items.Count; i++)
               Assert.Equal(items[i], _receivedInfos[i]);
        }


        [Theory]
        [InlineData(5)]
        [InlineData(10)]
        [InlineData(100)]
        [Trait("Category", "Add Item(s)")]
        public async Task AddEmptyItemsTest(int count)
        {
            List<StoreInfo> receivedInfo = new(count);
            void GetItem(BaseRequestModel storeInfo)
            {
                receivedInfo.Add(storeInfo as StoreInfo);
            }

            List<StoreInfo> items = new(count);
            for (int i = 0; i < count; i++)
            {
                StoreInfo storeInfo = new("", "/");
                items.Add(storeInfo);
            }

            var updatesQueue = new UpdatesQueue();
            await updatesQueue.AddItemsAsync(items);
            updatesQueue.ItemAdded += GetItem;

            await Task.Delay(DelayTime);
            Assert.Equal(items.Count, receivedInfo.Count);
            for (int i = 0; i < items.Count; i++)
                Assert.Equal(items[i], receivedInfo[i]);

        }


        private void GetItem(BaseRequestModel item)
        {
            _receivedInfos.Add(item as StoreInfo);
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
