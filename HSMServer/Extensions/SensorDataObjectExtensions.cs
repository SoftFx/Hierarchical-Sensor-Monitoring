using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HSMServer.DataLayer.Model;

namespace HSMServer.Extensions
{
    public static class SensorDataObjectExtensions
    {
        public static string ToShortString(this SensorDataObject sensorDataObject)
        {
            return $"Path = {sensorDataObject.Path}, Time = {sensorDataObject.Time}";
        }
    }
}
