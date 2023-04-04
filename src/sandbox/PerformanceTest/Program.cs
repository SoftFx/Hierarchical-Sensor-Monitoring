using HSMSensorDataObjects.FullDataObject;
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
    internal class Program
    {
        private const int UsersCount = 10;
        private const int SensorDatasCount = 1000;
        private const int UserSensorsCount = 100;
        private const int DefaultDelay = 100;

        private const string Address = "https://localhost";
        private const int Port = 44330;

        [ThreadStatic]
        private static string _key;

        private static void Main(string[] args)
        {
            _key = args.Length > 0 ? args[0] : "6f4da85f-f40c-42b5-8f8c-2c2ddb164fd4";

            for (int i = 0; i < UsersCount; ++i)
                ThreadPool.QueueUserWorkItem(OpenConnection, i);

            Console.ReadKey();
        }

        private static async void OpenConnection(object args)
        {
            int index = (int)args;

            HttpClientHandler handler = new()
            {
                ServerCertificateCustomValidationCallback = (message, certificate2, arg3, arg4) => true
            };

            var client = new HttpClient(handler, false);

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
                    Key = "5a4a421e-899f-42f7-a855-1423d35cddb2",
                    Path = $"user{userId}/double_sensor{i}",
                    Status = HSMSensorDataObjects.SensorStatus.Ok,
                });
            }

            for (int i = 0; i < SensorDatasCount; ++i)
            {
                for (int j = 0; j < UserSensorsCount; ++j)
                {
                    sensors[j].Time = DateTime.UtcNow;
                    sensors[j].DoubleValue = random.NextDouble();

                    try
                    {
                        string jsonString = JsonSerializer.Serialize(sensors[j]);
                        var data = new StringContent(jsonString, Encoding.UTF8, "application/json");

                        var result = await client.PostAsync($"{Address}:{Port}/api/sensors/double", data);

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
                    sensors[i].DoubleValue = 2;

                    string jsonString = JsonSerializer.Serialize(sensors[i]);
                    var data = new StringContent(jsonString, Encoding.UTF8, "application/json");

                    var result = await client.PostAsync($"{Address}:{Port}/api/sensors/double", data);

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
