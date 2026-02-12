using HSMCommon.Model;
using System;


namespace HSMServer.Core.Model
{
    public sealed class BooleanValuesStorage : ValuesStorage<BooleanValue>
    {
        public BooleanValuesStorage(Func<BaseValue> getFirstValue, Func<BaseValue> getLastValue) : base(getFirstValue, getLastValue)
        {
        }
    }
}
