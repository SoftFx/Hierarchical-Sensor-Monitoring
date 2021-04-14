using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json;
using HSMClientWPFControls.ConnectorInterface;
using HSMClientWPFControls.Objects;
using HSMClientWPFControls.ViewModel;
using HSMSensorDataObjects.TypedDataObject;
using OxyPlot;
using OxyPlot.Axes;

namespace HSMClient.Dialog
{
    public class ClientBoolTimeValueModel : ClientNumericTimeValueModel
    {
        public ClientBoolTimeValueModel(ISensorHistoryConnector connector, MonitoringSensorViewModel sensor) : base(connector, sensor)
        {
        }

        protected override Collection<DataPoint> ConvertToDataPoints(List<SensorHistoryItem> historyItems)
        {
            Collection<DataPoint> result = new Collection<DataPoint>();
            historyItems.Reverse();
            foreach (var item in historyItems)
            {
                BoolSensorData typedData = JsonSerializer.Deserialize<BoolSensorData>(item.SensorValue);
                result.Add(new DataPoint(DateTimeAxis.ToDouble(item.Time), typedData.BoolValue ? 1 : 0));
            }

            return result;
        }
    }
}
