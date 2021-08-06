using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HSMDatabase.EnvironmentDatabase;

namespace HSMDatabase.DatabaseWorkCore
{
    internal class DatabaseCore
    {
        private readonly IEnvironmentDatabase _environmentDatabase;

        public DatabaseCore()
        {
            _environmentDatabase = new EnvironmentDatabaseWorker("");
        }
    }
}
