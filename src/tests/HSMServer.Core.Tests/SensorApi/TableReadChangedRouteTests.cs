using HSMCommon.Model;
using HSMCommon.TaskResult;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Requests;
using HSMServer.Core.TableSensors;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using HSMServer.ServerConfiguration;
using HSMTableBlocks;
using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace HSMServer.Core.Tests.SensorApi
{
    public sealed class TableReadChangedRouteFixture : DatabaseFixture
    {
        protected override string DatabaseFolder => nameof(TableReadChangedRouteTests);
    }


    // The read route of table sensors (#1845) when the store changed under every plan of the read (#1847): a 503 with a
    // Retry-After of a second, on a real Kestrel listener. The text is written out on purpose: it is the contract.
    public class TableReadChangedRouteTests : MonitoringCoreTestsBase<TableReadChangedRouteFixture>
    {
        private static readonly DateTime T0 = new(2026, 11, 25, 9, 0, 0, DateTimeKind.Utc);

        private readonly string _root = $"r{Guid.NewGuid():N}"[..12];


        public TableReadChangedRouteTests(TableReadChangedRouteFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture)
        {
        }


        [Fact]
        public async Task AStoreThatChangedUnderEveryPlan_Is503_WithRetryAfterOfASecond()
        {
            var path = $"{_root}/q";
            var registered = await _valuesCache.AddOrUpdateSensorAsync(new SensorAddOrUpdateRequest(TestProductsManager.ProductId, path)
            {
                Update = new SensorUpdate { Id = Guid.Empty },
                Type = SensorType.Table,
                TypeIsExplicit = true,
                Schema = new TableSchema([new TableSchemaColumn("time", TableColumnType.Time), new TableSchemaColumn("bid", TableColumnType.Double)]),
            });
            Assert.True(registered.IsOk, registered.Error);

            var tables = new ChangedTableBlocks();
            await using var host = await SensorApiTestHost.StartAsync(_valuesCache, new CollectorSessionsConfig(), tables: tables);

            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var http = new HttpClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(host.HttpBase, "tableHistory"))
            {
                Content = new StringContent($"{{\"Path\":\"{path}\",\"From\":\"{T0:O}\",\"To\":\"{T0.AddHours(1):O}\",\"Format\":1}}", Encoding.UTF8, "application/json"),
            };

            request.Headers.Add("ClientName", "table-read-tests");
            request.Headers.Add("Key", TestProductsManager.TestProductKey.Id);

            using var response = await http.SendAsync(request, deadline.Token);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal("1", string.Join(",", response.Headers.GetValues("Retry-After")));
            Assert.Equal("text/plain; charset=utf-8", response.Content.Headers.ContentType?.ToString());
            Assert.Equal("The table store changed during the read; the request can be sent again.", await response.Content.ReadAsStringAsync(deadline.Token));
            Assert.Equal(1, tables.Reads);
        }


        // A block service whose every read met a change of the store on each of its plans.
        private sealed class ChangedTableBlocks : ITableBlockService
        {
            private int _reads;

            public int Reads => Volatile.Read(ref _reads);

            public TableBlockPutResult Put(Guid productId, Guid producerId, long producerSequence, byte[] block) => throw new NotSupportedException();

            public TaskResult<TableRows> Read(Guid sensorId, DateTime from, DateTime to, int? maxRows = null, CancellationToken token = default)
            {
                Interlocked.Increment(ref _reads);
                throw new TableReadChangedException("The store changed under every plan (made by the test).");
            }
        }
    }
}
