using System;

namespace HSMServer.Datasources.Aggregators
{
    public interface ILinePointState<T>
    {
        public DateTime Time { get; init; }

        public T Value { get; init; }
    }


    public interface ILinePoint<T>
    {
        public void SetNewState(ref readonly T state);
    }
}