using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HSMDataCollector.Alerts;
using HSMDataCollector.Core;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using HSMSensorDataObjects.SensorRequests;

namespace DatacollectorSandbox
{
    internal class Program
    {
        private static readonly Random _random = new Random(1123213);
        private static IDataCollector _collector;

        private static int _timeout = 1000;

        private static IInstantValueSensor<double> _baseDouble, _priorityDouble;
        private static IInstantValueSensor<bool> _baseBool, _priorityBool;
        private static IInstantValueSensor<int> _baseInt, _priorityInt;
        private static IInstantValueSensor<string> _baseString, _priorityString;
        private static IInstantValueSensor<Version> _baseVersion, _priorityVersion;
        private static IInstantValueSensor<TimeSpan> _baseTime, _priorityTime;

        private static ILastValueSensor<int> _lastInt;
        private static ILastValueSensor<double> _lastDouble;
        private static ILastValueSensor<bool> _lastBool;
        private static ILastValueSensor<string> _lastString;
        private static ILastValueSensor<Version> _lastVersion;
        private static ILastValueSensor<TimeSpan> _lastSpan;

        private static IMonitoringRateSensor _rate, _rateCustom, _rateM1, _rateM5;
        private static IBarSensor<int> _intBar, _intBarCustom, _intBarM1, _intBarM5;
        private static IBarSensor<double> _doubleBar, _doubleBarCustom, _doubleBarM1, _doubleBarM5;

        private static IFileSensor _fileSensor, _fileSensorCustom, _fileSensorCustom2, _fileSensorByPath;

        private static INoParamsFuncSensor<double> _funcSensor, _funcSensorCustom, _funcSensorM1, _funcSensorM5;
        private static IParamsFuncSensor<double, int> _paramSensor, _paramSensorCustom, _paramSensorM1, _paramSensorM5;

        static ConcurrentDictionary<string,int> _queuesInfo = new ConcurrentDictionary<string,int>();


        static async Task Main(string[] args)
        {

            var tokenSource = new CancellationTokenSource();


            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            var collectorOptions = new CollectorOptions()
            {
                //ServerAddress = "hsm.dev.soft-fx.eu",
                AccessKey = "8590351e-4752-4591-b1b9-80753d3e5542", //local key
                Module = "Collector 3.4.0",
                ComputerName = "LocalMachine",
            };

            _collector = new DataCollector(collectorOptions).AddNLog(new HSMDataCollector.Logging.LoggerOptions() { WriteDebug = true });

            _collector.Windows.AddAllDefaultSensors(GetVersion());

            await _collector.Start();


            _collector.Windows.SubscribeToWindowsServiceStatus(new ServiceSensorOptions() { ServiceName = "CaddyServer", IsHostService = false, SensorLocation = SensorLocation.Product,  SensorPath = $"{_collector.ComputerName}/CaddyService" });
            //_collector.Windows.SubscribeToWindowsServiceStatus("CaddyServer");


            // bool result = _collector.Windows.UnsubscribeWindowsServiceStatus(new ServiceSensorOptions() { IsHostService = false, SensorLocation = SensorLocation.Product, SensorPath = $"{_collector.ComputerName}/CaddyService" });

            //Console.WriteLine(result);

            //var sensor = _collector.CreateEnumSensor($"enum/test", new EnumSensorOptions
            //{
            //    EnumOptions = new List<HSMSensorDataObjects.EnumOption> {
            //        new HSMSensorDataObjects.EnumOption(1, "Stopped", " Service Stopped ", Color.FromArgb(0xFF0000)),
            //        new HSMSensorDataObjects.EnumOption(2, "Starting", "Service starting", Color.FromArgb(0xBFFFBF)),
            //        new HSMSensorDataObjects.EnumOption(3, "Started", "Service started", Color.FromArgb(0x00FF00)),
            //        new HSMSensorDataObjects.EnumOption(4, "Stopping", "Service Stopping", Color.FromArgb(0x809EFF)),
            //    },
            //    SensorLocation = SensorLocation.Module,
            //});

            while (true)
            {
                _baseInt = _collector.CreateIntSensor("instant/ Test/int");
                _baseInt.AddValue(GetInt());

                Thread.Sleep(1000);
            }


            //while (true)
            //{
            //    sensor.AddValue(1);
            //    Thread.Sleep(5000);
            //    sensor.AddValue(2);
            //    Thread.Sleep(5000);
            //    sensor.AddValue(3);
            //    Thread.Sleep(5000);
            //    sensor.AddValue(4);
            //    Thread.Sleep(5000);
            //}

            var sens1 = _collector.CreateIntSensor("test_default", new InstantSensorOptions { SensorLocation = SensorLocation.Module });
            sens1.AddValue(1);

            var sens2 = _collector.CreateIntSensor($"{collectorOptions.ComputerName}/.computer/test_computer", new InstantSensorOptions { SensorLocation = SensorLocation.Product });
            sens2.AddValue(1);

            var sens3 = _collector.CreateIntSensor("/.module/test_module", new InstantSensorOptions { SensorLocation = SensorLocation.Module });
            sens3.AddValue(1);

            var sens4 = _collector.CreateIntSensor("test_root", new InstantSensorOptions { SensorLocation = SensorLocation.Product });
            sens4.AddValue(1);


            //Console.ReadKey();
            //return;

            var instantPriority = new InstantSensorOptions()
            {
                Alerts = new List<InstantAlertTemplate>()
                {
                    AlertsFactory.IfStatus(HSMSensorDataObjects.SensorRequests.AlertOperation.IsError).ThenSetSensorError().Build(),
                },

                Description = "Test description",
                IsPrioritySensor = true,
            };


            _baseInt = _collector.CreateIntSensor("instant/int");
            _priorityInt = _collector.CreateIntSensor("instant_priority/int", instantPriority);

            _baseBool = _collector.CreateBoolSensor("instant/bool");
            _priorityBool = _collector.CreateBoolSensor("test default", instantPriority);

            _baseDouble = _collector.CreateDoubleSensor("instant/double");
            _priorityDouble = _collector.CreateDoubleSensor("instant_priority/double", instantPriority);

            _baseString = _collector.CreateStringSensor("instant/string");
            _priorityString = _collector.CreateStringSensor("instant_priority/string", instantPriority);

            _baseVersion = _collector.CreateVersionSensor("instant/version");
            _priorityVersion = _collector.CreateVersionSensor("instant_priority/version", instantPriority);

            _baseTime = _collector.CreateTimeSensor("instant/time");
            _priorityTime = _collector.CreateTimeSensor("instant_priority/time", instantPriority);

            _lastInt = _collector.CreateLastValueIntSensor("instant_last/int");
            _lastDouble = _collector.CreateLastValueDoubleSensor("instant_last/double");
            _lastBool = _collector.CreateLastValueBoolSensor("instant_last/bool");
            _lastSpan = _collector.CreateLastValueTimeSpanSensor("instant_last/time");
            _lastString = _collector.CreateLastValueStringSensor("instant_last/string");
            _lastVersion = _collector.CreateLastValueVersionSensor("instant_last/version");

            _rate = _collector.CreateRateSensor("special/rate");
            _rateCustom = _collector.CreateRateSensor("special/rate_custom", new RateSensorOptions() { Description = "Test descriptions" });
            _rateM1 = _collector.CreateM1RateSensor("special/rate_m1");
            _rateM5 = _collector.CreateM5RateSensor("special/rate_m5");

            _intBar = _collector.CreateIntBarSensor("bar/intBar");
            _intBarCustom = _collector.CreateIntBarSensor("bar/intBar_custom", new BarSensorOptions() { Description = "Test bar description" });
            _intBarM1 = _collector.Create1MinIntBarSensor("bar/intBar_m1", "test m1");
            _intBarM5 = _collector.Create5MinIntBarSensor("bar/intBar_m5", "test m5");

            _doubleBar = _collector.CreateDoubleBarSensor("bar/doubleBar");
            _doubleBarCustom = _collector.CreateDoubleBarSensor("bar/doubleBar_custom", new BarSensorOptions() { Description = "Test bar description" });
            _doubleBarM1 = _collector.Create1MinDoubleBarSensor("bar/doubleBar_m1", description: "test m1");
            _doubleBarM5 = _collector.Create5MinDoubleBarSensor("bar/doubleBar_m5", description: "test m5");

            _fileSensor = _collector.CreateFileSensor("special/file");
            _fileSensorCustom = _collector.CreateFileSensor("special/file_custom_option", new FileSensorOptions() { Description = "asdasd" });
            _fileSensorCustom2 = _collector.CreateFileSensor("special/file_custom_args", "test");
            _fileSensorByPath = _collector.CreateFileSensor("special/file_by_path");

            _funcSensor = _collector.CreateFunctionSensor("functions/simple", GetDouble);
            _funcSensorCustom = _collector.CreateFunctionSensor("functions/simple_custom", GetDouble, new FunctionSensorOptions() { Description = "test descr for func" });
            _funcSensorM1 = _collector.Create1MinNoParamsFuncSensor("functions/simple_m1", "test1", GetDouble);
            _funcSensorM5 = _collector.Create5MinNoParamsFuncSensor("functions/simple_m5", "test5", GetDouble);


            double GetAverage(List<int> list) => list.Count > 0 ? list.Sum() / list.Count : 0;

            _paramSensor = _collector.CreateValuesFunctionSensor<double, int>("functions/param", GetAverage);
            _paramSensorCustom = _collector.CreateValuesFunctionSensor<double, int>("functions/param_custom", GetAverage, new ValuesFunctionSensorOptions() { Description = "test descr for func" });
            _paramSensorM1 = _collector.Create1MinParamsFuncSensor<double, int>("functions/param_m1", "test1", GetAverage);
            _paramSensorM5 = _collector.Create5MinParamsFuncSensor<double, int>("functions/param_m5", "test5", GetAverage);

            _ = PushByTimer(tokenSource);

            bool needRestart = false;

            var process = Process.GetCurrentProcess();

            while (true)
            {
                //Console.WriteLine("wait");
                //var s = Console.ReadLine();

                //if (s == "e")
                //    break;

                //if (s == "r")
                //{
                //    needRestart = true;
                //    break;
                //}

                //Console.WriteLine(GetCurrentThreads(process));

                await PushValue();
            }

            tokenSource.Cancel();
            await _collector.Stop();

            if (needRestart)
            {
                Console.WriteLine("Wait restart");

                Console.ReadLine();

                Console.WriteLine("Run restart");

                await _collector.Start();

                tokenSource = new CancellationTokenSource();

                _ = PushByTimer(tokenSource);

                while (true)
                {
                    Console.WriteLine("wait");
                    var s = Console.ReadLine();

                    if (s == "e")
                        break;

                    if (s == "r")
                    {
                        needRestart = true;
                        break;
                    }

                    PushValue();
                }

                await _collector.Stop();
            }

            Console.WriteLine("wait dispose");
            Console.ReadLine();

            _collector.Dispose();
        }

        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Console.WriteLine(e);
        }

        private static async Task PushByTimer(CancellationTokenSource source)
        {
            while (!source.IsCancellationRequested)
            {
                _rate.AddValue(GetDouble());
                _rateM1.AddValue(GetDouble());
                _rateM5.AddValue(GetDouble());
                _rateCustom.AddValue(GetDouble());

                _intBar.AddValue(GetInt());
                _intBarCustom.AddValue(GetInt());
                _intBarM1.AddValue(GetInt());
                _intBarM5.AddValue(GetInt());

                _doubleBar.AddValue(GetDouble());
                _doubleBarCustom.AddValue(GetDouble());
                _doubleBarM1.AddValue(GetDouble());
                _doubleBarM5.AddValue(GetDouble());

                _paramSensor.AddValue(GetInt());
                _paramSensorCustom.AddValue(GetInt());
                _paramSensorM1.AddValue(GetInt());
                //_paramSensorM5.AddValue(GetInt());

                await Task.Delay(_timeout);
            }
        }


        private static async Task PushValue()
        {
            _baseInt.AddValue(GetInt());
            //_priorityInt.AddValue(GetInt());

            _baseBool.AddValue(_random.Next() % 2 == 0);
            //_priorityBool.AddValue(_random.Next() % 2 == 0);

            _baseDouble.AddValue(GetDouble());
            //_priorityDouble.AddValue(GetDouble());

            _baseString.AddValue(GetRandomString(10));
           // _priorityString.AddValue(GetRandomString(10));

            _baseTime.AddValue(TimeSpan.FromTicks(_random.Next()));
            //_priorityTime.AddValue(TimeSpan.FromTicks(_random.Next()));

            _baseVersion.AddValue(GetVersion());
           // _priorityVersion.AddValue(GetVersion());


            _lastInt.AddValue(GetInt());
            _lastDouble.AddValue(GetDouble());
            _lastBool.AddValue(_random.Next() % 2 == 0);
            _lastString.AddValue(GetRandomString(10));
            _lastVersion.AddValue(GetVersion());
            _lastSpan.AddValue(TimeSpan.FromTicks(_random.Next()));

            //_fileSensor.AddValue(GetRandomString(100));
            //_fileSensorCustom.AddValue(GetRandomString(100));
            //_fileSensorCustom2.AddValue(GetRandomString(100));
            //await _fileSensorByPath.SendFile(Path.Combine(Environment.CurrentDirectory, "TEST LOCAL FILE.txt"));

            Thread.Sleep(_timeout);

        }


        private static int GetInt() => _random.Next() % 10000;

        private static double GetDouble() => _random.NextDouble() * 1000;

        private static string GetRandomString(int len)
        {
            var sb = new StringBuilder(len);

            for (int i = 0; i < len; ++i)
                sb.Append((char)(_random.Next() % 26 + 'a'));

            return sb.ToString();
        }

        private static Version GetVersion()
        {
            int GetNumber() => _random.Next() % 10;

            return new Version(GetNumber(), GetNumber(), GetNumber());
        }

        private static int GetCurrentThreads(Process process)
        {
            int runningThreadsCount = 0;
            foreach (ProcessThread thread in process.Threads)
            {
                if (thread.ThreadState == System.Diagnostics.ThreadState.Running)
                {
                    runningThreadsCount++;
                }
            }

            return runningThreadsCount;
        }

    }

}
