using HSMServer.Core.Model;
using HSMServer.Core.SensorsUpdatesQueue;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace HSMServer.Core.Tests.UpdatesQueueTests
{
    public class UpdatesQueueTests
    {
        [Fact]
        [Trait("UnitTest", "UnitTest")]
        public async void AddItemTest()
        {
            StoreInfo receivedInfo = default;
            void getItem(List<StoreInfo> storeInfo)
            {
                receivedInfo = storeInfo[0];
            }
            StoreInfo storeInfo = new() { Key = "", Path = "/", BaseValue = new IntegerValue() { Value = 5} };
            var UpdatesQueue = new UpdatesQueue();
            UpdatesQueue.AddItem(storeInfo);
            UpdatesQueue.NewItemsEvent += getItem;
            await Task.Delay(100);
            Assert.Equal(storeInfo, receivedInfo);
        }

        [Fact]
        [Trait("UnitTest", "UnitTest")]
        public async void AddEmptyItemTest()
        {
            StoreInfo receivedInfo = default;
            void getItem(List<StoreInfo> storeInfo)
            {
                receivedInfo = storeInfo[0];
            }
            StoreInfo storeInfo = new();
            var UpdatesQueue = new UpdatesQueue();
            UpdatesQueue.AddItem(storeInfo);
            UpdatesQueue.NewItemsEvent += getItem;
            await Task.Delay(100);
            Assert.Equal(storeInfo, receivedInfo);
        }

        [Fact]
        [Trait("UnitTest", "UnitTest")]
        public async void AddItemsTest()
        {
            List<StoreInfo> receivedInfo = default;
            void getItem(List<StoreInfo> storeInfo)
            {
                receivedInfo = storeInfo;
            }

            List<StoreInfo> items = new();
            for (int i = 0; i < 10; i++) 
            {
                StoreInfo storeInfo = new() { Key = "", Path = "/", BaseValue = new IntegerValue() { Value = i } };
                items.Add(storeInfo);
            }

            var UpdatesQueue = new UpdatesQueue();
            UpdatesQueue.AddItems(items);
            UpdatesQueue.NewItemsEvent += getItem;
            await Task.Delay(100);
            Assert.Equal(items, receivedInfo);
        }

        [Fact]
        [Trait("UnitTest", "UnitTest")]
        public async void AddEmptyItemsTest()
        {
            List<StoreInfo> receivedInfo = default;
            void getItem(List<StoreInfo> storeInfo)
            {
                receivedInfo = storeInfo;
            }

            List<StoreInfo> items = new();
            for (int i = 0; i < 10; i++)
            {
                StoreInfo storeInfo = new();
                items.Add(storeInfo);
            }

            var UpdatesQueue = new UpdatesQueue();
            UpdatesQueue.AddItems(items);
            UpdatesQueue.NewItemsEvent += getItem;
            await Task.Delay(100);
            Assert.Equal(items, receivedInfo);
        }
    }
}
