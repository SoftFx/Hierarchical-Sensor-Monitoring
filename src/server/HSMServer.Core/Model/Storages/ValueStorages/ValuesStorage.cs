using HSMServer.Core.DataLayer;
using System.Collections.Generic;

namespace HSMServer.Core.Model
{
    public abstract class ValuesStorage<T> where T : BaseValue
    {
        public List<T> Values { get; } = new();

        public IDatabaseCore Database { get; init; }


        internal void AddValue(byte[] valueBytes)
        {
            var value = EntityConverter.ConvertSensorData<T>(valueBytes);

            if (value != null)
                Values.Add((T)value);
        }
    }
}
