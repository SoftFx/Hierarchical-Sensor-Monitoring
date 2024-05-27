using System;


namespace HSMServer.Core.Model
{
    [Flags]
    public enum StatisticsOptions : int
    {
        None = 0,
        EMA = 1,
    }
}
