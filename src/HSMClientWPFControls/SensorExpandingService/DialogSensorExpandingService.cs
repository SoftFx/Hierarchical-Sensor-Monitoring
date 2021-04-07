using System;
using System.Collections.Generic;
using HSMClientWPFControls.ConnectorInterface;
using HSMClientWPFControls.Model.SensorDialog;
using HSMClientWPFControls.Objects;
using HSMClientWPFControls.View.SensorDialog;
using HSMClientWPFControls.ViewModel;
using HSMClientWPFControls.ViewModel.SensorDialog;

namespace HSMClientWPFControls.SensorExpandingService
{
    public class DialogSensorExpandingService : ISensorExpandingService
    {
        struct DialogSensorViewTypes
        {
            public Type ViewType;
            public Type ViewModelType;
            public Type ModelType;
        }

        private IDialogModelFactory _factory;
        private Dictionary<SensorTypes, DialogSensorViewTypes> _typeToDialogDictionary;
        public DialogSensorExpandingService(IDialogModelFactory factory)
        {
            _factory = factory;
            _typeToDialogDictionary = new Dictionary<SensorTypes, DialogSensorViewTypes>();
        }

        public void RegisterDialog(SensorTypes sensorType, Type viewType, Type modelType)
        {
            _typeToDialogDictionary[sensorType] = new DialogSensorViewTypes()
            {
                ModelType = modelType,
                ViewType = viewType
            };
            _factory?.RegisterModel(sensorType, modelType);
        }
        
        public void Expand(MonitoringSensorViewModel sensor)
        {
            SensorControl view = null;
            DialogSensorViewTypes VVMTypes = _typeToDialogDictionary[sensor.SensorType];

            object viewObj = Activator.CreateInstance(VVMTypes.ViewType);
            view = viewObj as SensorControl;

            ISensorDialogModel model = _factory.ConstructModel(sensor);
            DialogViewModel viewModel = null;

            if (view != null)
            {
                if (VVMTypes.ViewModelType != null)
                {
                    object viewModelObj = Activator.CreateInstance(VVMTypes.ViewModelType, model);
                    viewModel = viewModelObj as DialogViewModel;
                }
                else
                {
                    viewModel = view.ConstructDefaultViewModel(model);
                }
            }
            else
            {
                view = new DefaultValuesListSensorView();
                viewModel = new DefaultValuesListSensorViewModel(model);
            }

            string title = $"{sensor.Product}/{sensor.Path}";
            DialogWindow dw = new DialogWindow(view, viewModel, title);
            dw.Show();
        }
    }
}
