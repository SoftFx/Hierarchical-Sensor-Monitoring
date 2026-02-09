using HSMCommon.Model;
using System;


namespace HSMServer.Core.Model;

public class VersionValueStorage : ValuesStorage<VersionValue>
{
    public VersionValueStorage(Func<BaseValue> getFirstValue, Func<BaseValue> getLastValue) : base(getFirstValue, getLastValue)
    {
    }

    protected override int CacheSize => 20;
}