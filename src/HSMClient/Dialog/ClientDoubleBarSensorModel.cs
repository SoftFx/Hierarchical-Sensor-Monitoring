using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using HSMClientWPFControls.ConnectorInterface;
using HSMClientWPFControls.Model;
using HSMClientWPFControls.Objects;
using HSMClientWPFControls.ViewModel;
using HSMSensorDataObjects.TypedDataObject;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;

namespace HSMClient.Dialog
{
    public class ClientDoubleBarSensorModel : ClientBarSensorModel
    {
        public ClientDoubleBarSensorModel(ISensorHistoryConnector connector, MonitoringSensorViewModel sensor) : base(connector, sensor)
        {
        }

        protected override Collection<BoxPlotItem> ConvertToBoxPlotItems(List<SensorHistoryItem> historyItems)
        {
            //historyItems.Reverse();
            List<DoubleBarSensorData> serializedData = RangeByTime(historyItems);
            Collection<BoxPlotItem> result = new Collection<BoxPlotItem>();
            FillCollection(result, serializedData);

            return result;
        }
        
        private List<DoubleBarSensorData> RangeByTime(List<SensorHistoryItem> historyItems)
        {
            var serializedList = historyItems.Select(l => JsonSerializer.Deserialize<DoubleBarSensorData>(l.SensorValue)).ToList();
            List<DoubleBarSensorData> finalList = new List<DoubleBarSensorData>();
            for (int i = 0; i < serializedList.Count; ++i)
            {
                if (i < serializedList.Count - 1)
                {
                    if (serializedList[i].StartTime != serializedList[i + 1].StartTime)
                    {
                        finalList.Add(serializedList[i]);
                        continue;
                    }

                    if (serializedList[i].EndTime >= serializedList[i + 1].EndTime)
                    {
                        finalList.Add(serializedList[i]);
                        ++i;
                    }
                    else
                    {
                        finalList.Add(serializedList[i + 1]);
                        i += 2;
                    }
                }
                else
                {
                    finalList.Add(serializedList[i]);
                }
            }

            return finalList;
        }

        private void FillCollection(Collection<BoxPlotItem> collection, List<DoubleBarSensorData> list)
        {
            foreach (var boxValue in list)
            {
                double firstQuant = boxValue.Percentiles.FirstOrDefault(p => Math.Abs(p.Percentile - 0.25) < double.Epsilon).Value;
                double thirdQuant = boxValue.Percentiles.FirstOrDefault(p => Math.Abs(p.Percentile - 0.75) < double.Epsilon).Value;
                double median = boxValue.Percentiles.FirstOrDefault(p => Math.Abs(p.Percentile - 0.5) < double.Epsilon).Value;
                double lowerWhisker = firstQuant - (1.5 * (thirdQuant - firstQuant));
                double upperWhisker = thirdQuant + (1.5 * (thirdQuant - firstQuant));

                //DateTime time = new DateTime(1970, 1, 1).AddMilliseconds(boxValue.StartTime.ToUniversalTime().ToTimestamp().Seconds);
                double x = DateTimeAxis.ToDouble(boxValue.StartTime);
                BoxPlotItem plotItem = new BoxPlotItem(x, lowerWhisker, firstQuant, median,
                    thirdQuant, upperWhisker);
                plotItem.Mean = boxValue.Mean;

                collection.Add(plotItem);
            }
        }
    }
}
