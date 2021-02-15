using HSMClientWPFControls.Model.SensorDialog;
using HSMClientWPFControls.ViewModel.SensorDialog;

namespace HSMClientWPFControls.View.SensorDialog
{
    /// <summary>
    /// Interaction logic for BoolSensorView.xaml
    /// </summary>
    public partial class BoolSensorView : SensorControl
    {
        public BoolSensorView()
        {
            InitializeComponent();
        }

        public override DialogViewModel ConstructDefaultViewModel(ISensorDialogModel model)
        {
            return new BoolSensorViewModel(model);
        }
    }
}
