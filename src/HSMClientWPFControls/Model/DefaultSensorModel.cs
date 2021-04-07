using System;
using System.Collections.Generic;
using System.Text;
using HSMClientWPFControls.Bases;
using HSMClientWPFControls.Objects;

namespace HSMClientWPFControls.Model
{
    public class DefaultSensorModel : NotifyingBase
    {
        private readonly SensorHistoryItem _item;
        public DefaultSensorModel(SensorHistoryItem item)
        {
            _item = item;
        }

        public string TimeString => _item.Time.ToString("G");

        public string TypedValueString => _item.SensorValue;
    }
}
