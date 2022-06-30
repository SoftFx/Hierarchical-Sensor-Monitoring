using HSMServer.Core.DataLayer;
using System.Collections.Generic;

namespace HSMServer.Core.Model
{
    public abstract class ValuesStorage<T> where T : BaseValue, new()
    {
        protected IDatabaseCore _database;


        public List<T> Values { get; } = new();


        internal ValuesStorage(IDatabaseCore database)
        {
            _database = database;
        }


        internal void InitializeValues(string productName, string path)
        {
            var value = _database.GetLatestValue<T>(productName, path);
            if (value != null)
                Values.Add(value);
        }
    }
}
