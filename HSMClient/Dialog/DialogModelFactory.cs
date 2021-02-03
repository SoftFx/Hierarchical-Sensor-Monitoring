using System;
using System.Collections.Generic;
using System.Text;
using HSMClientWPFControls.ConnectorInterface;
using HSMClientWPFControls.Model.SensorDialog;
using HSMClientWPFControls.Objects;
using HSMClientWPFControls.SensorExpandingService;
using HSMClientWPFControls.ViewModel;

namespace HSMClient.Dialog
{
    class DialogModelFactory : IDialogModelFactory
    {
        private Type _modelType;
        private ISensorHistoryConnector _connector;
        public DialogModelFactory(ISensorHistoryConnector connector)
        {
            _connector = connector;
        }
        public ISensorDialogModel ConstructModel(MonitoringSensorViewModel sensor)
        {
            return Activator.CreateInstance(_modelType, _connector, sensor) as ISensorDialogModel;
        }

        public void RegisterModel(SensorTypes sensorType, Type modelType)
        {
            _modelType = modelType;
        }
    }
}
