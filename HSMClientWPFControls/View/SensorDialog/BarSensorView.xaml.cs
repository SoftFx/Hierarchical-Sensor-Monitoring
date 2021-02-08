using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using HSMClientWPFControls.Model.SensorDialog;
using HSMClientWPFControls.ViewModel.SensorDialog;

namespace HSMClientWPFControls.View.SensorDialog
{
    /// <summary>
    /// Interaction logic for BarSensorView.xaml
    /// </summary>
    public partial class BarSensorView : SensorControl
    {
        public BarSensorView()
        {
            InitializeComponent();
        }

        public override DialogViewModel ConstructDefaultViewModel(ISensorDialogModel model)
        {
            return new BarSensorViewModel(model);
        }
    }
}
