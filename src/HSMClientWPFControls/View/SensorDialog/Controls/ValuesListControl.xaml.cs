using System.Collections;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace HSMClientWPFControls.View.SensorDialog.Controls
{
    /// <summary>
    /// Interaction logic for ValuesListControl.xaml
    /// </summary>
    public partial class ValuesListControl : UserControl
    {
        public static readonly DependencyProperty ItemsProperty =
            DependencyProperty.Register("Items", typeof(IEnumerable), typeof(ValuesListControl));

        //[Bindable(true)]
        public IEnumerable Items
        {
            get => (IEnumerable) GetValue(ItemsProperty);
            set => SetValue(ItemsProperty, value);
        }
        public ValuesListControl()
        {
            InitializeComponent();
        }
    }
}
