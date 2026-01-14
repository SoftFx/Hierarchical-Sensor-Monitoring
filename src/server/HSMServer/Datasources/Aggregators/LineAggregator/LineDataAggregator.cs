using System;
using HSMCommon.Model;
using HSMServer.Dashboards;


namespace HSMServer.Datasources.Aggregators
{
    public interface IDataAggregator<TValue>
    {
        IDataAggregator<TValue> AttachConverter(Func<BaseValue, TValue> toChartValue);
    }


    public abstract class LineDataAggregator<TValue, TPoint, TState> : BaseDataAggregator<TState>, IDataAggregator<TValue>
        where TPoint : BaseChartValue<TValue>, ILinePoint<TState>, new()
        where TState : struct, ILinePointState<TValue>
    {
        private protected Func<BaseValue, TValue> _toChartValue;


        public IDataAggregator<TValue> AttachConverter(Func<BaseValue, TValue> toChartValue)
        {
            _toChartValue = toChartValue;

            return this;
        }


        protected override BaseChartValue BuildNewPoint() => new TPoint();

        protected override TState BuildState(BaseValue rawValue) => new()
        {
            Value = _toChartValue(rawValue),
            Time = rawValue.Time,
        };

        protected override void ApplyState(BaseChartValue rawPoint, TState state)
        {
            if (rawPoint is TPoint point)
                point.SetNewState(ref state);
        }


        protected NotImplementedException BuildNotSupportedException(PlottedProperty property) =>
            new($"Line aggregation by {property} is not supported for {GetType().FullName} aggregator");
    }
}
