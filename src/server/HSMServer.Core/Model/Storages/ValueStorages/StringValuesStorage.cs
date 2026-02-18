using HSMCommon.Model;
using System;


namespace HSMServer.Core.Model
{
    public sealed class StringValuesStorage : ValuesStorage<StringValue>
    {
        protected override int CacheSize => 20;
    }
}
