using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HSMDataCollector.PerformanceSensor.CustomFuncSensor
{
    internal abstract class FuncSensorBase<T>
    {
        protected Func<T> Function;
        protected Timer _valuesTimer;
        internal FuncSensorBase(Func<T> function, int timeout = 150000)
        {
            Function = function;
        }

        //private void 
    }
}
