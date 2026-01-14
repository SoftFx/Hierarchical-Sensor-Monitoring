using System;
using MemoryPack;
using HSMCommon.Model;


namespace HSMDatabase.AccessManager.Formatters
{
    public class MemoryPackFormatter : IFormatter
    {
        public BaseValue Deserialize(byte[] data)
        {
            try
            {
                return MemoryPackSerializer.Deserialize<BaseValue>(data);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to deserialize", ex);
            }
        }

        public byte[] Serialize(BaseValue value)
        {
            try
            {
                return MemoryPackSerializer.Serialize(value);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to serialize", ex);
            }
        }
    }
}
