using System;
using System.Collections.Generic;
using System.Text;
using HSMClientWPFControls.Bases;
using HSMClientWPFControls.Objects;

namespace HSMClientWPFControls.Model
{
    public class DefaultSensorModel : NotifyingBase
    {
        private readonly MonitoringSensorUpdate _updateObj;
        public DefaultSensorModel(MonitoringSensorUpdate update)
        {
            _updateObj = update;
        }

        public string TimeString
        {
            get => _updateObj.Time.ToString("G");
        }

        public string TypedValueString
        {
            get => Encoding.UTF8.GetString(_updateObj.DataObject);
        }
    }
}
