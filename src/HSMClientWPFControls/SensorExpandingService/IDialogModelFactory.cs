using System;
using HSMClientWPFControls.Model.SensorDialog;
using HSMClientWPFControls.Objects;
using HSMClientWPFControls.ViewModel;

namespace HSMClientWPFControls.SensorExpandingService
{
    public interface IDialogModelFactory
    {
        public ISensorDialogModel ConstructModel(MonitoringSensorViewModel sensor);
        public void RegisterModel(SensorTypes sensorType, Type viewModelType);
    }
}