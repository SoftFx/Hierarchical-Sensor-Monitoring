using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using HSMServer.Core.Model.Requests;
using HSMServer.Core.SensorsUpdatesQueue;
using HSMCommon.Model;


namespace HSMServer.Core.Tests.UpdatesQueueTests
{
    public class UpdatesQueueTests
    {

        private List<IUpdateRequest> _receivedInfos;

        private readonly ITestOutputHelper _output;

        public UpdatesQueueTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        [Trait("Category", "Add Item(s)")]
        public async Task AddItemTest()
        {
            AddSensorValueRequest receivedInfo = default;
            void GetItem(IUpdatesQueue queue, IUpdateRequest item)
            {
                receivedInfo = item as AddSensorValueRequest;
            }

            AddSensorValueRequest storeInfo = BuildStoreInfo(0);
            await using var updatesQueue = new UpdatesQueue("Name", GetItem);


            await updatesQueue.ProcessRequestAsync(storeInfo);

            Assert.Equal(storeInfo, receivedInfo);
        }

        [Fact]
        [Trait("Category", "Add Item(s)")]
        public async Task AddEmptyItemTest()
        {
            AddSensorValueRequest receivedInfo = default;
            void GetItem(IUpdatesQueue queue, IUpdateRequest item)
            {
                receivedInfo = item as AddSensorValueRequest;
            }

            AddSensorValueRequest storeInfo = BuildStoreInfo(10);
            await using var updatesQueue = new UpdatesQueue("Name", GetItem);

            await updatesQueue.ProcessRequestAsync(storeInfo);

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

            List<AddSensorValueRequest> items = AddItemsToList(count);

            await using var updatesQueue = new UpdatesQueue("Name", GetItem);

            foreach (var item in items)
                await updatesQueue.ProcessRequestAsync(item);

            Assert.Equal(items.Count, _receivedInfos.Count);
            for (int i = 0; i < items.Count; i++)
               Assert.Equal(items[i], _receivedInfos[i]);
        }


        [Theory]
        [InlineData(5)]
        [InlineData(10)]
        [InlineData(50)]
        [Trait("Category", "Add Item(s)")]
        public async Task AddEmptyItemsTest(int count)
        {
            List<AddSensorValueRequest> receivedInfo = new(count);
            void GetItem(IUpdatesQueue queue, IUpdateRequest storeInfo)
            {
                receivedInfo.Add(storeInfo as AddSensorValueRequest);
            }

            List<AddSensorValueRequest> items = new(count);
            for (int i = 0; i < count; i++)
            {
                AddSensorValueRequest storeInfo = BuildStoreInfo(1);
                items.Add(storeInfo);
            }

            await using var updatesQueue = new UpdatesQueue("Name", GetItem);

            foreach (var item in items)
                await updatesQueue.ProcessRequestAsync(item);


            Assert.Equal(items.Count, receivedInfo.Count);
            for (int i = 0; i < items.Count; i++)
                Assert.Equal(items[i], receivedInfo[i]);

        }


        private void GetItem(IUpdatesQueue queue, IUpdateRequest item)
        {
            _receivedInfos.Add(item as AddSensorValueRequest);
        }

        private static List<AddSensorValueRequest> AddItemsToList(int count)
        {
            List<AddSensorValueRequest> items = new(count);

            for (int i = 0; i < count; i++)
            {
                AddSensorValueRequest storeInfo = BuildStoreInfo(i);
                items.Add(storeInfo);
            }

            return items;
        }

        private static AddSensorValueRequest BuildStoreInfo(int value)
        {
            return new(Guid.NewGuid(), Guid.NewGuid(), "/", new IntegerValue() { Value = value });
        }
    }
}
