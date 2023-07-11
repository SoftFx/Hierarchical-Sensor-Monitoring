using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PerformanceTest
{
    internal static class HttpClientTest
    {
        private const string Key = "5a4a421e-899f-42f7-a855-1423d35cddb2";

        private const int UsersCount = 10;
        private const int SensorDatasCount = 1000;
        private const int UserSensorsCount = 100;
        private const int DefaultDelay = 100;


        internal static void Start()
        {
            for (int i = 0; i < UsersCount; ++i)
                ThreadPool.QueueUserWorkItem(OpenConnection, i);
        }

        private static async void OpenConnection(object args)
        {
            int index = (int)args;

            HttpClientHandler handler = new()
            {
                ServerCertificateCustomValidationCallback = (message, certificate2, arg3, arg4) => true
            };

            var client = new HttpClient(handler, false);
            client.DefaultRequestHeaders.Add(nameof(BaseRequest.Key), Key);

            var watch = new Stopwatch();
            watch.Start();

            await SendDoubleSensor(client, new Random(134134278 + index), index);

            watch.Stop();
            Console.WriteLine($"user {index}, watch {watch.ElapsedMilliseconds / 1000.0} s");

            client.Dispose();
        }

        private static async Task SendDoubleSensor(HttpClient client, Random random, int userId)
        {
            var sensors = new List<DoubleSensorValue>(UserSensorsCount);
            for (int i = 0; i < UserSensorsCount; ++i)
            {
                sensors.Add(new DoubleSensorValue()
                {
                    Path = $"user{userId}/double_sensor{i}",
                    Status = SensorStatus.Ok,
                });
            }

            for (int i = 0; i < SensorDatasCount; ++i)
            {
                for (int j = 0; j < UserSensorsCount; ++j)
                {
                    sensors[j].Time = DateTime.UtcNow;
                    sensors[j].Value = random.NextDouble();

                    try
                    {
                        string jsonString = JsonSerializer.Serialize(sensors[j]);
                        var data = new StringContent(jsonString, Encoding.UTF8, "application/json");

                        var result = await client.PostAsync($"{Program.Address}:{Program.Port}/api/sensors/double", data);

                        if (!result.IsSuccessStatusCode)
                        {
                            Console.WriteLine($"user: {userId} - {result}");
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"user: {userId} exception - {ex}");
                        return;
                    }
                }
            }

            for (int i = 0; i < sensors.Count; ++i)
            {
                try
                {
                    sensors[i].Time = DateTime.UtcNow;
                    sensors[i].Value = 2;

                    string jsonString = JsonSerializer.Serialize(sensors[i]);
                    var data = new StringContent(jsonString, Encoding.UTF8, "application/json");

                    var result = await client.PostAsync($"{Program.Address}:{Program.Port}/api/sensors/double", data);

                    if (!result.IsSuccessStatusCode)
                    {
                        Console.WriteLine($"user: {userId} - {result}");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"user: {userId} exception - {ex}");
                    return;
                }
            }
        }
    }
}
