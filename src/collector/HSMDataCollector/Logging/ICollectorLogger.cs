using System;

namespace HSMDataCollector.Logging
{
    public interface ICollectorLogger
    {
        void Debug(string message);

        void Info(string message);

        void Error(string message);

        void Error(Exception ex);
    }
}
