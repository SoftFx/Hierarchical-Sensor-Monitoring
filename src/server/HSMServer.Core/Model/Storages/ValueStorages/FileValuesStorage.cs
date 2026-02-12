using HSMCommon.Model;
using HSMServer.Core.Extensions;
using System;


namespace HSMServer.Core.Model
{
    public sealed class FileValuesStorage : ValuesStorage<FileValue>
    {
        public FileValuesStorage(Func<BaseValue> getFirstValue, Func<BaseValue> getLastValue) : base(getFirstValue, getLastValue)
        {
        }

        protected override int CacheSize => 1;


        internal override void AddValueBase(FileValue value) => base.AddValueBase(value.DecompressContent());

        internal override void AddValue(FileValue value) => base.AddValue(value.CompressContent());
    }
}