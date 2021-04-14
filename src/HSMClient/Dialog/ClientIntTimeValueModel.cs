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
    public class ClientIntTimeValueModel : ClientNumericTimeValueModel
    {
        public ClientIntTimeValueModel(ISensorHistoryConnector connector, MonitoringSensorViewModel sensor)
            : base(connector, sensor)
        {

        }

        protected override Collection<DataPoint> ConvertToDataPoints(List<SensorHistoryItem> historyItems)
        {
            Collection<DataPoint> result = new Collection<DataPoint>();
            historyItems.Reverse();
            foreach (var item in historyItems)
            {
                IntSensorData typedData = JsonSerializer.Deserialize<IntSensorData>(item.SensorValue);
                result.Add(new DataPoint(DateTimeAxis.ToDouble(item.Time), typedData.IntValue));
            }

            return result;
        }
    }
}
