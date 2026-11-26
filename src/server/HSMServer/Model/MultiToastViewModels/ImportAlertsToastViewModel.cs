using System.Collections.Generic;
using System.Text;

namespace HSMServer.Model.MultiToastViewModels
{
    public sealed class ImportAlertsToastViewModel
    {
        private readonly Dictionary<string, LimitedQueue<string>> _errorUpdates = new();

        // An import file repeats product names and paths across its alert groups: each one is reported
        // once per error, so the limited list is not filled with repeats of the first.
        private readonly HashSet<(string Error, string Name)> _reported = new();


        public void AddError(string error, string sensorName)
        {
            if (!_reported.Add((error, sensorName)))
                return;

            if (!_errorUpdates.ContainsKey(error))
                _errorUpdates[error] = new(10);

            _errorUpdates[error].Enqueue(sensorName);
        }

        public string ToResponse()
        {
            var builder = new StringBuilder(1 << 5);

            foreach (var (errorMessage, sensors) in _errorUpdates)
                sensors.ToBuilder(builder, $"{errorMessage} - ");

            return builder.ToString();
        }
    }
}
