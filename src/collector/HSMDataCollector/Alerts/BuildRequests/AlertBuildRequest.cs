using HSMSensorDataObjects;
using System.Collections.Generic;

namespace HSMDataCollector.Alerts
{
    public sealed class SpecialAlertBuildRequest : AlertBuildRequest { }


    public sealed class InstantAlertBuildRequest : AlertBuildRequest { }


    public sealed class BarAlertBuildRequest : AlertBuildRequest { }


    public abstract class AlertBuildRequest
    {
        public List<AlertConditionBuildRequest> Conditions { get; set; }

        public SensorStatus Status { get; set; }


        public string Template { get; set; }

        public string Icon { get; set; }


        public bool IsDisabled { get; set; }


        protected internal AlertBuildRequest() { }
    }
}