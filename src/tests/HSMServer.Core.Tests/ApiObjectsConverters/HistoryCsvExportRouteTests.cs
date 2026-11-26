using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using HSMCommon.Model;
using HSMSensorDataObjects.SensorValueRequests;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Requests;
using HSMServer.Core.Tests.Controllers;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using HSMServer.Core.Tests.SensorApi;
using HSMServer.ServerConfiguration;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace HSMServer.Core.Tests.ApiObjectsConverters
{
    public sealed class SensorApiCsvExportFixture : DatabaseFixture
    {
        protected override string DatabaseFolder => nameof(SensorApiCsvExportTests);
    }


    // #1886: the Sensor API's history export (historyFile) is read by scripts: a String value, a comment and a file's
    // content come out as they are stored, as before #1886.
    public class SensorApiCsvExportTests : MonitoringCoreTestsBase<SensorApiCsvExportFixture>
    {
        private const string ClientName = "csv-export";

        private static readonly string Bidi = ((char)0x202E).ToString();

        private readonly Guid _product = TestProductsManager.ProductId;
        private readonly string _key = TestProductsManager.TestProductKey.Id;
        private readonly string _root = $"ce{Guid.NewGuid():N}"[..12];


        public SensorApiCsvExportTests(SensorApiCsvExportFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture)
        {
        }


        [Fact]
        public async Task String_values_and_comments_come_out_as_they_are_stored()
        {
            var path = await CreateSensorAsync("strings", SensorType.String);
            await AddAsync(new StringSensorValue { Path = path, Value = "-5", Comment = "a" + Bidi + "b", Time = DateTime.UtcNow.AddSeconds(-2) });
            await AddAsync(new StringSensorValue { Path = path, Value = "=1+1", Time = DateTime.UtcNow.AddSeconds(-1) });

            var csv = await ExportAsync(path);

            var separator = CultureInfo.CurrentUICulture.TextInfo.ListSeparator;
            Assert.Contains($"{separator}\"-5\"{separator}", csv);
            Assert.Contains($"{separator}\"=1+1\"{separator}", csv);
            Assert.Contains($"\"a{Bidi}b\"", csv, StringComparison.Ordinal);
            Assert.DoesNotContain("'", csv);
        }

        [Fact]
        public async Task A_files_base64_content_comes_out_as_it_is()
        {
            var path = await CreateSensorAsync("files", SensorType.File);
            byte[] content = [0xF8, 0x01, 0x02];
            await AddAsync(new FileSensorValue { Path = path, Name = "-dump", Extension = "bin", Value = [.. content], Time = DateTime.UtcNow.AddSeconds(-1) });

            var csv = await ExportAsync(path);

            Assert.Contains($"\"{Convert.ToBase64String(content)}\"", csv);
            Assert.Contains("\"-dump\"", csv);
            Assert.DoesNotContain("'", csv);
        }


        private async Task<string> ExportAsync(string path)
        {
            await using var host = await SensorApiTestHost.StartAsync(_valuesCache, new CollectorSessionsConfig());
            var (status, csv) = await host.PostAsync("historyFile",
                $"{{\"Path\":\"{path}\",\"From\":\"2000-01-01T00:00:00Z\",\"To\":\"2100-01-01T00:00:00Z\",\"FileName\":\"h\",\"Extension\":\"csv\"}}", _key, ClientName);

            Assert.Equal(HttpStatusCode.OK, status);
            return csv;
        }

        private async Task AddAsync(SensorValueBase value)
        {
            var result = await _valuesCache.AddSensorValueAsync(Guid.Parse(_key), _product, value);
            Assert.True(result.IsOk, result.Error);
        }

        private async Task<string> CreateSensorAsync(string name, SensorType type)
        {
            var path = $"{_root}/{name}";
            var result = await _valuesCache.AddOrUpdateSensorAsync(new SensorAddOrUpdateRequest(_product, path)
            {
                Update = new SensorUpdate { Id = Guid.Empty },
                Type = type,
                TypeIsExplicit = true,
            });

            Assert.True(result.IsOk, result.Error);
            return path;
        }
    }


    // #1886: the History tab's Export button downloads a file a person opens in a spreadsheet: a String value and a
    // comment that a spreadsheet would read as a formula are written as text, without bidirectional controls; a file's
    // base64 content stays as it is.
    public class WebCsvExportTests
    {
        private static readonly DateTime Time = new(2026, 11, 26, 0, 0, 0, DateTimeKind.Utc);


        [Fact]
        public async Task String_values_and_comments_are_written_as_text()
        {
            var tree = new WebAccessFixture();
            var sensor = tree.AddOwnSensor("strings", SensorType.String,
            [
                new StringValue { Time = Time.AddSeconds(1), ReceivingTime = Time.AddSeconds(1), Value = "=1+1", Status = SensorStatus.Ok },
                new StringValue { Time = Time, ReceivingTime = Time, Value = "-5", Comment = "a" + (char)0x202E + "b", Status = SensorStatus.Ok },
            ]);

            var csv = await ExportAsync(tree, sensor);

            Assert.Contains("\"'-5\"", csv);
            Assert.Contains("\"'=1+1\"", csv);
            Assert.Contains("\"a" + (char)0xFFFD + "b\"", csv, StringComparison.Ordinal);
            Assert.DoesNotContain((char)0x202E, csv);
        }

        [Fact]
        public async Task A_files_base64_content_is_written_as_it_is()
        {
            byte[] content = [0xF8, 0x01, 0x02];
            var tree = new WebAccessFixture();
            var sensor = tree.AddOwnSensor("files", SensorType.File,
            [
                new FileValue { Time = Time, ReceivingTime = Time, Name = "-dump", Extension = "bin", Value = content, OriginalSize = content.Length, Status = SensorStatus.Ok },
            ]);

            var csv = await ExportAsync(tree, sensor);

            Assert.Contains($"\"{Convert.ToBase64String(content)}\"", csv);
            Assert.DoesNotContain($"'{Convert.ToBase64String(content)}", csv);
            Assert.Contains("\"'-dump\"", csv);
        }


        private static async Task<string> ExportAsync(WebAccessFixture tree, BaseSensorModel sensor)
        {
            var export = Assert.IsType<FileContentResult>(await tree.History(tree.Member).ExportHistory(sensor.Id.ToString(), 0, false, default, default));

            return Encoding.UTF8.GetString(export.FileContents);
        }
    }
}
