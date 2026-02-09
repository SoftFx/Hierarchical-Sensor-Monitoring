using HSMCommon.Model;
using System;


namespace HSMServer.Core.Model
{
    public sealed class StringValuesStorage : ValuesStorage<StringValue>
    {
        public StringValuesStorage(Func<BaseValue> getFirstValue, Func<BaseValue> getLastValue) : base(getFirstValue, getLastValue)
        {
        }

        protected override int CacheSize => 20;
    }
}
