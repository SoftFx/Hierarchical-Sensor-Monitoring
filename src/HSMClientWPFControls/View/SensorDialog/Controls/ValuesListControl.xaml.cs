using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using HSMClientWPFControls.Model;

namespace HSMClientWPFControls.View.SensorDialog.Controls
{
    /// <summary>
    /// Interaction logic for ValuesListControl.xaml
    /// </summary>
    public partial class ValuesListControl : UserControl
    {
        public static readonly DependencyProperty ItemsProperty =
            DependencyProperty.Register("Items", typeof(Collection<DefaultSensorModel>), typeof(ValuesListControl));

        [Bindable(true)]
        public Collection<DefaultSensorModel> Items
        {
            get => (Collection<DefaultSensorModel>) GetValue(ItemsProperty);
            set => SetValue(ItemsProperty, value);
        }
        public ValuesListControl()
        {
            //DataContext = this;
            InitializeComponent();
        }
    }
}
