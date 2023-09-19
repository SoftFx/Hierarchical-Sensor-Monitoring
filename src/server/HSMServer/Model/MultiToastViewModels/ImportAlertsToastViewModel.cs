using System.Collections.Generic;
using System.Text;

namespace HSMServer.Model.MultiToastViewModels
{
    public sealed class ImportAlertsToastViewModel
    {
        private readonly Dictionary<string, LimitedQueue<string>> _errorUpdates;


        public void AddError(string error, string sensorName)
        {
            if (!_errorUpdates.ContainsKey(error))
                _errorUpdates[error] = new(10);

            _errorUpdates[error].Enqueue(sensorName);
        }

        public string ToResponse()
        {
            var builder = new StringBuilder(1 << 5);

            foreach (var (errorMessage, sensors) in _errorUpdates)
                sensors.ToBuilder(builder, errorMessage);

            return builder.ToString();
        }
    }
}
