using HSMDataCollector.Core;
using HSMSensorDataObjects;
using System;

namespace HSMDataCollector.SyncQueue
{
    internal class CommandsQueue : SyncQueue<BaseRequest>
    {
        public CommandsQueue(CollectorOptions options) : base(options, TimeSpan.FromSeconds(1)) { }
    }
}