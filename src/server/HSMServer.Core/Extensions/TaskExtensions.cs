using NLog;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HSMServer.Core.Extensions
{
    public static class TaskExtensions
    {
        public static void Forget(this Task task, ILogger? logger = null)
        {
            task.ContinueWith(t =>
            {
                if (t.IsFaulted && logger != null)
                {
                    logger.Error(t.Exception, "Unhandled exception in background task");
                }
                else if (t.IsFaulted)
                {
                    Console.Error.WriteLine(t.Exception);
                }
            }, TaskContinuationOptions.OnlyOnFaulted);
        }
    }
}
