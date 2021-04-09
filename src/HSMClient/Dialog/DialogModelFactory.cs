using System;
using System.Collections.Generic;
using System.Text;
using HSMClient.Common.Logging;
using HSMClientWPFControls.ConnectorInterface;
using HSMClientWPFControls.Model.SensorDialog;
using HSMClientWPFControls.Objects;
using HSMClientWPFControls.SensorExpandingService;
using HSMClientWPFControls.ViewModel;

namespace HSMClient.Dialog
{
    class DialogModelFactory : IDialogModelFactory
    {
        private readonly ISensorHistoryConnector _connector;
        private readonly Dictionary<SensorTypes, Type> _sensorModelType;

        public DialogModelFactory(ISensorHistoryConnector connector)
        {
            _connector = connector;
            _sensorModelType = new Dictionary<SensorTypes, Type>();
        }
        public ISensorDialogModel ConstructModel(MonitoringSensorViewModel sensor)
        {
            try
            {
                if (_sensorModelType.ContainsKey(sensor.SensorType))
                {
                    if (_sensorModelType[sensor.SensorType] != typeof(object))
                    {
                        return Activator.CreateInstance(_sensorModelType[sensor.SensorType], _connector, sensor) as ISensorDialogModel;
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Error($"Failed to create model instance: {e}");
            }
            return new ClientDefaultValuesListSensorModel(_connector, sensor);
        }

        public void RegisterModel(SensorTypes sensorType, Type viewModelType)
        {
            _sensorModelType[sensorType] = viewModelType;
        }
    }
}
