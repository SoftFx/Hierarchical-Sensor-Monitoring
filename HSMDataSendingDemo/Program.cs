using System;
using System.Threading;
using HSMDataCollector.Core;

namespace HSMDataSendingDemo
{
    class Program
    {
        static void Main(string[] args)
        {
            IDataCollector collector = new DataCollector("2201cd7959dc87a1dc82b8abf29f48", "https://localhost", 44330);

            var boolSensor = collector.CreateBoolSensor("sensors/DataSender/Boolean");

            boolSensor.AddValue(true);

            var doubleSensor = collector.CreateDoubleSensor("sensors/DataSender/Double");

            int c = 10000;

            for (int i = 0; i < c; i++)
            {
                doubleSensor.AddValue((double)(DateTime.Now.Ticks / 4413431.0));               
                Thread.Sleep(new TimeSpan(0,0,0,5));
            }
        }
    }
}
