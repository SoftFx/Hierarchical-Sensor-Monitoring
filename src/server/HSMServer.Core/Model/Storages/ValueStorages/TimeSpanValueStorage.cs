using HSMCommon.Model;
using System;

namespace HSMServer.Core.Model;

public class TimeSpanValueStorage : ValuesStorage<TimeSpanValue>
{
    public TimeSpanValueStorage(Func<BaseValue> getFirstValue, Func<BaseValue> getLastValue) : base(getFirstValue, getLastValue)
    {
    }
}