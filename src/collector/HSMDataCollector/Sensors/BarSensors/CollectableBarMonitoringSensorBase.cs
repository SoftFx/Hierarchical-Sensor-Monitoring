using System;
using HSMDataCollector.Options;


namespace HSMDataCollector.DefaultSensors
{
    public abstract class CollectableBarMonitoringSensorBase<BarType, T> : BarMonitoringSensorBase<BarType, T>
        where BarType : MonitoringBarBase<T>, new()
        where T : struct
    {
        protected CollectableBarMonitoringSensorBase(BarSensorOptions options) : base(options) { }


        protected abstract T GetBarData();

        protected override void CollectBar(object _)
        {
            try
            {
                base.CollectBar(_);

                _internalBar.AddValue(GetBarData());
            }
            catch (Exception ex)
            {
                ThrowException(ex);
            }
        }
    }
}