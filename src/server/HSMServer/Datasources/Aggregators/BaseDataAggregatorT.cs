using HSMServer.Dashboards;
using System;
using System.Collections.Generic;

namespace HSMServer.Datasources.Aggregators
{
    public abstract class BaseDataAggregator<TState> : BaseDataAggregator
        where TState : struct
    {
        private readonly LinkedList<TState> _lastPointStates;
        private Func<TState, TState, TState> _getNewState;


        internal override void Setup(SourceSettings settings)
        {
            _getNewState = GetNewStateFactory(settings.Property);

            base.Setup(settings);
        }


        protected abstract Func<TState, TState, TState> GetNewStateFactory(PlottedProperty property);
    }
}
