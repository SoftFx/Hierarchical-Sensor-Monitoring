using HSMDataCollector.DefaultSensors.MonitoringSensorBase.BarBuilder;
using HSMDataCollector.Extensions;
using HSMDataCollector.Options;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace HSMDataCollector.DefaultSensors
{
    public abstract class BarMonitoringSensorBase<BarType, BarValueType> : BarMonitoringSensorBase<BarType, BarValueType, BarValueType>
        where BarType : MonitoringBarBase<BarValueType>, new()
        where BarValueType : struct, IComparable<BarValueType>
    {
        protected BarMonitoringSensorBase(BarSensorOptions options, int precision = 2) : base(options, precision)
        {
        }

        protected sealed override void AddValueToBar(BarType bar, BarValueType value)
        {
            bar.AddValue(value);
        }
    }

    public abstract class BarSensorBase<BarType, BarValueType> : BarMonitoringSensorBase<BarType, BarValue<BarValueType>, BarValueType>
        where BarType : MonitoringBarBase<BarValueType>, new()
        where BarValueType : struct, IComparable<BarValueType>
    {
        protected BarSensorBase(BarSensorOptions options, int precision = 2) : base(options, precision)
        {
        }

        protected sealed override void AddValueToBar(BarType bar, BarValue<BarValueType> value)
        {
            bar.AddValue(value);
        }
    }

    public abstract class BarMonitoringSensorBase<BarType, ValueType, BarValueType> : MonitoringSensorBase<BarType>
        where BarType : MonitoringBarBase<BarValueType>, new()
        where BarValueType : struct, IComparable<BarValueType>
    {
        private readonly TimeSpan _barPeriod;
        private readonly TimeSpan _collectBarPeriod;

        private BarType _internalBar;
        private Timer _collectTimer;

        private readonly int _precision;


        protected sealed override TimeSpan TimerDueTime => _receiveDataPeriod.GetTimerDueTime();


        protected BarMonitoringSensorBase(BarSensorOptions options, int precision = 2) : base(options)
        {
            _barPeriod = options.BarPeriod;
            _collectBarPeriod = options.CollectBarPeriod;
            _precision = precision;
            BuildNewBar();
        }


        internal override async Task<bool> Init()
        {
            var isInitialized = await base.Init();

            if (isInitialized)
                _collectTimer = new Timer(CollectBar, null, _collectBarPeriod, _collectBarPeriod);

            return isInitialized;
        }

        internal override async Task Stop()
        {
            _collectTimer?.Dispose();

            await base.Stop();

            OnTimerTick();
        }


        protected abstract ValueType GetBarData();

        protected abstract void AddValueToBar(BarType bar, ValueType value);

        protected sealed override BarType GetValue() => _internalBar.Complete() as BarType;

        protected sealed override BarType GetDefaultValue()
        {
            return new BarType()
            {
                OpenTime = _internalBar?.OpenTime ?? DateTime.UtcNow,
                CloseTime = _internalBar?.CloseTime ?? DateTime.UtcNow,
                Count = 1,
            };
        }

        private void CollectBar(object _)
        {
            try
            {
                if (_internalBar.CloseTime < DateTime.UtcNow)
                {
                    OnTimerTick();
                    BuildNewBar();
                }

                AddValueToBar(_internalBar, GetBarData());
            }
            catch { }
        }

        private void BuildNewBar()
        {
            _internalBar = new BarType();
            _internalBar.Init(_barPeriod, _precision);
        }
    }
}
