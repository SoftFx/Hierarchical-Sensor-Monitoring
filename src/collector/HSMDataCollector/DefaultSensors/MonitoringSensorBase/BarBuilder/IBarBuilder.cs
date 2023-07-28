using HSMSensorDataObjects.SensorValueRequests;
using System;
using System.Collections.Generic;
using System.Text;

namespace HSMDataCollector.DefaultSensors.MonitoringSensorBase.BarBuilder
{
    public interface IBarBuilder<AddValueType, BarValueType>
    {
        void AddValue(AddValueType value);
        void FillBarFields(BarSensorValueBase<BarValueType> bar);
    }
}
