using System;
using System.Collections.Generic;
using System.Text;
using HSMClientWPFControls.Model.SensorDialog;
using HSMClientWPFControls.View.SensorDialog;
using HSMClientWPFControls.ViewModel;
using HSMClientWPFControls.ViewModel.SensorDialog;

namespace HSMClientWPFControls.SensorExpandingService
{
    public class SensorExpandingService : ISensorExpandingService
    {
        private IDialogModelFactory _factory;
        public SensorExpandingService(IDialogModelFactory factory)
        {
            _factory = factory;
        }
        public void Expand(MonitoringSensorBaseViewModel sensor)
        {
            SensorControl view = null;
            object viewObj = Activator.CreateInstance(typeof(DefaultValuesListSensorView));
            view = viewObj as SensorControl;

            ISensorDialogModel model = _factory.ConstructModel(sensor);

            DialogViewModel viewModel = new DefaultValuesListSensorViewModel(model);

            DialogWindow dw = new DialogWindow(view, viewModel, $"{sensor.Name} from product {sensor.Product}");
            dw.Show();
        }
    }
}
