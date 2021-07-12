using System;

namespace HSMDataCollector.PublicInterface
{
    [Obsolete("08.07.2021. Use IBarSensor.")]
    public interface IDoubleBarSensor
    {
        void AddValue(double value);
    }
}