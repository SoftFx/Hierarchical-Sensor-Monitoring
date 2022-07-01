using HSMServer.Core.DataLayer;
using System.Collections.Generic;

namespace HSMServer.Core.Model
{
    public abstract class ValuesStorage<T> where T : BaseValue
    {
        public List<T> Values { get; } = new();

        public IDatabaseCore Database { get; init; }


        internal void InitializeValues(string productName, string path)
        {
            var value = Database.GetLatestValue<T>(productName, path);

            if (value != null)
                Values.Add(value);
        }
    }
}
