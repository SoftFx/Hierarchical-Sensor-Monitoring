using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HSMDatabase.AccessManager
{
    public interface IIntervalDatabase : IDisposable
    {
        string Name { get; }

        long From { get; }

        long To { get; }


        bool Contains(long time);

        bool Overlaps(long from, long to);

    }
}
