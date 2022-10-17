using HSMServer.Core.Model;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Model.History
{
    public class HistoryValuesViewModel
    {
        private readonly SensorType _sensorType;


        public string EncodedId { get; }

        public string OldestValueTime { get; }

        public List<HistoryValueViewModel> Values { get; }

        public bool IsBarSensor => _sensorType is SensorType.IntegerBar or SensorType.DoubleBar;


        internal HistoryValuesViewModel(string encodedId, int type, List<BaseValue> values)
        {
            _sensorType = (SensorType)type;

            EncodedId = encodedId;
            OldestValueTime = values.LastOrDefault()?.Time.ToUniversalTime().ToString("O") ?? string.Empty;
            Values = values.Select(v => HistoryValueViewModel.Create(v, type)).ToList();
        }
    }
}
