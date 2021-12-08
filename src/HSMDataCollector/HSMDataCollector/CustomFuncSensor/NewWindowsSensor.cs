using HSMDataCollector.Core;
using HSMSensorDataObjects;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HSMDataCollector.CustomFuncSensor
{
    internal class NewWindowsSensor : OneParamFuncSensor<bool, bool>
    {
        private static readonly string _windiwsVersion;
        private static readonly DateTime _windowsLastUpdate;

        private readonly TimeSpan _timeToUpdate;

        static NewWindowsSensor()
        {
            //_windiwsVersion = ...
            //_windowsLastUpdate
        }

        public NewWindowsSensor(string path, string productKey, IValuesQueue queue, string description, TimeSpan timerSpan, 
               SensorType type, bool isLogging, TimeSpan maxTimePeriod) 
               : base(path, productKey, queue, GetDescription(description), timerSpan, type, funcToInvoke, isLogging)
        {
        }

        private bool UpdateFunction(List<bool> _)
        {
            return _windowsLastUpdate
        }

        private static string GetDescription(string userDes) => $"Version {_windiwsVersion}, LastUpdate {_windowsLastUpdate} {userDes}";
    }
}
