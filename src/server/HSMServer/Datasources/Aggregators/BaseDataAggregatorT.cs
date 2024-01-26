using HSMServer.Core.Model;
using HSMServer.Dashboards;
using System;
using System.Collections.Generic;

namespace HSMServer.Datasources.Aggregators
{
    public abstract class BaseDataAggregator<TState> : BaseDataAggregator
        where TState : struct
    {
        private readonly LinkedList<TState> _lastPointStates = new();
        private Func<TState, TState, TState> _getAggrState;


        internal override void Setup(SourceSettings settings)
        {
            _getAggrState = GetAggrStateFactory(settings.Property);

            base.Setup(settings);
        }


        protected abstract Func<TState, TState, TState> GetAggrStateFactory(PlottedProperty property);

        protected abstract void ApplyState(BaseChartValue point, TState state);

        protected abstract TState BuildState(BaseValue value);


        protected override void ReapplyValue(BaseChartValue point, BaseValue newValue)
        {
            var prevState = _lastPointStates.First?.Value;

            if (_lastPointStates.Count > 0)
                _lastPointStates.RemoveLast();

            RebuildLastState(point, newValue, prevState);
        }

        protected override void ApplyValue(BaseChartValue point, BaseValue newValue) =>
            RebuildLastState(point, newValue, _lastPointStates.Last?.Value);

        private void RebuildLastState(BaseChartValue point, BaseValue newValue, TState? prevState)
        {
            var newState = BuildState(newValue);

            if (prevState is not null)
                newState = _getAggrState(prevState.Value, newState);

            ApplyState(point, newState);

            _lastPointStates.AddLast(newState);

            while (_lastPointStates.Count > 2)
                _lastPointStates.RemoveFirst();
        }
    }
}