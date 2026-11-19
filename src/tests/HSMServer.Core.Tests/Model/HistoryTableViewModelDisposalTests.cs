using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HSMCommon.Model;
using HSMServer.Core.Cache;
using HSMSensorDataObjects.HistoryRequests;
using HSMServer.Core.Model;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Model.History;
using HSMServer.Model.Model.History;
using Moq;
using Xunit;
using TestSensorModelFactory = HSMServer.Core.Tests.Infrastructure.SensorModelFactory;

namespace HSMServer.Core.Tests.Model
{
    // #1804 review S2: a history table holds the page read it started (database iterators, and a lease on the week
    // instance it reads) until the read is disposed. A reload and the table's own disposal must end it.
    public class HistoryTableViewModelDisposalTests
    {
        [Fact]
        public async Task Reload_DisposesThePreviousRead_AndDispose_DisposesTheCurrentOne()
        {
            var model = TestSensorModelFactory.Build(EntitiesFactory.BuildSensorEntity(type: (byte)SensorType.Double));
            var reads = new List<Read>();

            var cache = new Mock<ITreeValuesCache>();
            cache.Setup(c => c.GetSensorValuesPage(model.Id, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<RequestOptions>()))
                 .Returns(() =>
                 {
                     var read = new Read();
                     reads.Add(read);
                     return read.Pages();
                 });

            var table = new HistoryTableViewModel(model);

            await table.Reload(cache.Object, new GetSensorHistoryRequest());
            Assert.False(reads[0].Disposed); // more pages to come: the read is still open

            await table.Reload(cache.Object, new GetSensorHistoryRequest());
            Assert.True(reads[0].Disposed);
            Assert.False(reads[1].Disposed);

            table.Dispose();
            Assert.True(reads[1].Disposed);
        }


        private sealed class Read
        {
            public bool Disposed { get; private set; }

            public async IAsyncEnumerable<List<BaseValue>> Pages()
            {
                try
                {
                    for (int i = 0; i < 10; i++)
                    {
                        await Task.Yield();
                        yield return [new DoubleValue { Value = i, Time = DateTime.UtcNow, ReceivingTime = DateTime.UtcNow }];
                    }
                }
                finally
                {
                    Disposed = true;
                }
            }
        }
    }
}
