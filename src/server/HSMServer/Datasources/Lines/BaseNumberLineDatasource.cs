using HSMServer.Core.Model;
using HSMServer.Datasources.Aggregators;
using HSMServer.Datasources.Lines;
using System;
using System.Numerics;

namespace HSMServer.Datasources
{
    public abstract class BaseNumberLineDatasource<TValue, TProp, TChart> : BaseLineDatasource<TValue, TProp, TChart>
        where TValue : BaseValue
        where TChart : INumber<TChart>
    {
        protected override BaseDataAggregator BuildDataAggregator(Func<BaseValue, TChart> converter) =>
            new LineDataAggregator<TChart>(converter);
    }
}