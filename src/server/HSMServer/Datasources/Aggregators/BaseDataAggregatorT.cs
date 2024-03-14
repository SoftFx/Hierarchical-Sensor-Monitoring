using HSMServer.Core.Model;
using HSMServer.Core.Model.Requests;
using HSMServer.Dashboards;
using System;
using System.Collections.Generic;

namespace HSMServer.Datasources.Aggregators
{
    public abstract class BaseDataAggregator<TState> : BaseDataAggregator
        where TState : struct
    {
        private readonly LinkedList<TState> _lastPointStates = new();
        private readonly object _lock = new object();

        private Func<TState, TState, TState> _getAggrState;


        internal override void Setup(SourceSettings settings)
        {
            _getAggrState = GetAggrStateFactory(settings.Property);

            base.Setup(settings);
        }


        protected abstract Func<TState, TState, TState> GetAggrStateFactory(PlottedProperty property);

        protected abstract void ApplyState(BaseChartValue point, TState state);

        protected abstract TState BuildState(BaseValue value);

        protected abstract BaseChartValue BuildNewPoint();


        internal override void RecalculateAggrSections(SensorHistoryRequest request)
        {
            lock (_lock)
            {
                _lastPointStates.Clear();

                base.RecalculateAggrSections(request);
            }
        }

        protected override BaseChartValue GetNewChartValue(BaseValue value)
        {
            lock (_lock)
            {
                _lastPointStates.Clear();

                return ApplyAndSaveState(BuildNewPoint(), BuildState(value));
            }
        }

        protected override void ReapplyValue(BaseChartValue point, BaseValue newValue)
        {
            lock (_lock)
            {
                var prevState = _lastPointStates.First?.Value;

                if (_lastPointStates.Count > 0)
                    _lastPointStates.RemoveLast();

                AggregateStates(point, newValue, prevState);
            }
        }

        protected override void ApplyValue(BaseChartValue point, BaseValue newValue) =>
            AggregateStates(point, newValue, _lastPointStates.Last?.Value);

        private void AggregateStates(BaseChartValue point, BaseValue newValue, TState? prevState)
        {
            lock (_lock)
            {
                var newState = BuildState(newValue);

                if (prevState is not null)
                    newState = _getAggrState(prevState.Value, newState);

                ApplyAndSaveState(point, newState);
            }
        }

        private BaseChartValue ApplyAndSaveState(BaseChartValue point, TState state)
        {
            lock (_lock)
            {
                ApplyState(point, state);

                _lastPointStates.AddLast(state);

                while (_lastPointStates.Count > 2)
                    _lastPointStates.RemoveFirst();

                return point;
            }
        }
    }
}