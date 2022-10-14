using HSMServer.Core.Model;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Model.History
{
    public class SensorValuesViewModel
    {
        public string EncodedId { get; }

        public SensorType SensorType { get; }

        public string OldestValueTime { get; }

        public List<SensorValueViewModel> Values { get; }


        internal SensorValuesViewModel(string encodedId, int type, List<BaseValue> values)
        {
            EncodedId = encodedId;
            SensorType = (SensorType)type;
            OldestValueTime = values.LastOrDefault()?.Time.ToUniversalTime().ToString("O") ?? "";
            Values = values.Select(v => SensorValueViewModel.Create(v, type)).ToList();
        }
    }
}
