using HSMClientWPFControls.Model.SensorDialog;
using HSMClientWPFControls.ViewModel.SensorDialog;

namespace HSMClientWPFControls.View.SensorDialog
{
    /// <summary>
    /// Interaction logic for NumericSensorView.xaml
    /// </summary>
    public partial class NumericSensorView : SensorControl
    {
        public NumericSensorView()
        {
            InitializeComponent();
        }

        public override DialogViewModel ConstructDefaultViewModel(ISensorDialogModel model)
        {
            return new NumericSensorViewModel(model);
        }
    }
}
