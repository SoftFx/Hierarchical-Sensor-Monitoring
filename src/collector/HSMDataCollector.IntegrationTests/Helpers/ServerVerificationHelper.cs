using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using HSMSensorDataObjects.HistoryRequests;

namespace HSMDataCollector.IntegrationTests.Helpers
{
    public class ServerVerificationHelper : IDisposable
    {
        private readonly HttpClient _client;
        private readonly TimeSpan _pollInterval = TimeSpan.FromMilliseconds(500);

        public ServerVerificationHelper(string serverAddress, ushort port, string accessKey)
        {
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
            };
            _client = new HttpClient(handler);
            _client.DefaultRequestHeaders.Add("Key", accessKey);
            _client.BaseAddress = new Uri($"https://{serverAddress}:{port}");
        }

        public async Task<string?> TryGetSensorHistoryAsync(string sensorPath, DateTime from, int count = 1)
        {
            try
            {
                var request = new HistoryRequest
                {
                    Path = sensorPath,
                    From = from,
                    Count = count,
                };

                var json = JsonSerializer.Serialize(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _client.PostAsync("/api/sensors/history", content);
                response.EnsureSuccessStatusCode();

                return await response.Content.ReadAsStringAsync();
            }
            catch
            {
                return null;
            }
        }

        public async Task<bool> WaitForValueAsync(string sensorPath, int expectedCount, TimeSpan timeout)
        {
            var start = DateTime.UtcNow;
            var from = DateTime.UtcNow.AddMinutes(-5);

            while (DateTime.UtcNow - start < timeout)
            {
                var historyJson = await TryGetSensorHistoryAsync(sensorPath, from, expectedCount);
                if (!string.IsNullOrEmpty(historyJson) && historyJson != "[]" && historyJson != "null")
                    return true;

                await Task.Delay(_pollInterval);
            }

            return false;
        }

        /// <summary>
        /// Waits for data and returns all value strings. The API returns values newest-first;
        /// this method reverses them to chronological (oldest-first) order.
        /// </summary>
        public async Task<List<string>> WaitForAndGetAllValuesAsync(string sensorPath, int expectedCount, TimeSpan timeout)
        {
            var start = DateTime.UtcNow;
            var from = DateTime.UtcNow.AddMinutes(-5);

            while (DateTime.UtcNow - start < timeout)
            {
                var historyJson = await TryGetSensorHistoryAsync(sensorPath, from, expectedCount);
                if (string.IsNullOrEmpty(historyJson) || historyJson == "[]" || historyJson == "null")
                {
                    await Task.Delay(_pollInterval);
                    continue;
                }

                var elements = JsonSerializer.Deserialize<JsonElement[]>(historyJson!);
                if (elements == null || elements.Length == 0)
                {
                    await Task.Delay(_pollInterval);
                    continue;
                }

                var values = new List<string>();
                foreach (var entry in elements)
                {
                    if (entry.TryGetProperty("Value", out var valueProp))
                        values.Add(valueProp.GetString() ?? "");
                }

                if (values.Count >= expectedCount)
                {
                    values.Reverse();
                    return values;
                }

                await Task.Delay(_pollInterval);
            }

            return new List<string>();
        }

        public async Task<List<BarValueResult>> WaitForAndGetAllBarValuesAsync(string sensorPath, int expectedCount, TimeSpan timeout)
        {
            var start = DateTime.UtcNow;
            var from = DateTime.UtcNow.AddMinutes(-5);

            while (DateTime.UtcNow - start < timeout)
            {
                var historyJson = await TryGetSensorHistoryAsync(sensorPath, from, expectedCount);
                if (string.IsNullOrEmpty(historyJson) || historyJson == "[]" || historyJson == "null")
                {
                    await Task.Delay(_pollInterval);
                    continue;
                }

                var elements = JsonSerializer.Deserialize<JsonElement[]>(historyJson!);
                if (elements == null || elements.Length == 0)
                {
                    await Task.Delay(_pollInterval);
                    continue;
                }

                var results = new List<BarValueResult>();
                foreach (var entry in elements)
                {
                    results.Add(new BarValueResult
                    {
                        Min = entry.GetProperty("Min").GetString(),
                        Max = entry.GetProperty("Max").GetString(),
                        Mean = entry.GetProperty("Mean").GetString(),
                        FirstValue = entry.TryGetProperty("FirstValue", out var first) ? first.GetString() : null,
                        LastValue = entry.TryGetProperty("LastValue", out var last) ? last.GetString() : null,
                    });
                }

                if (results.Count >= expectedCount)
                    return results;

                await Task.Delay(_pollInterval);
            }

            return new List<BarValueResult>();
        }

        public async Task<FileValueResult?> WaitForFileValueAsync(string sensorPath, TimeSpan timeout)
        {
            var start = DateTime.UtcNow;
            var from = DateTime.UtcNow.AddMinutes(-5);

            while (DateTime.UtcNow - start < timeout)
            {
                var historyJson = await TryGetSensorHistoryAsync(sensorPath, from, 1);
                if (string.IsNullOrEmpty(historyJson) || historyJson == "[]" || historyJson == "null")
                {
                    await Task.Delay(_pollInterval);
                    continue;
                }

                var values = JsonSerializer.Deserialize<JsonElement[]>(historyJson!);
                if (values is { Length: > 0 })
                {
                    var entry = values[0];
                    return new FileValueResult
                    {
                        FileName = entry.TryGetProperty("FileName", out var name) ? name.GetString() : null,
                        Extension = entry.TryGetProperty("Extension", out var ext) ? ext.GetString() : null,
                    };
                }

                await Task.Delay(_pollInterval);
            }

            return null;
        }

        public void Dispose()
        {
            _client?.Dispose();
        }
    }

    public class BarValueResult
    {
        public string? Min { get; set; }
        public string? Max { get; set; }
        public string? Mean { get; set; }
        public string? FirstValue { get; set; }
        public string? LastValue { get; set; }
    }

    public class FileValueResult
    {
        public string? FileName { get; set; }
        public string? Extension { get; set; }
    }
}
