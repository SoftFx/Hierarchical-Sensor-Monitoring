using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
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
using HSMClientWPFControls.Annotations;
using HSMClientWPFControls.Model.SensorDialog;
using HSMClientWPFControls.ViewModel.SensorDialog;
using LiveCharts;
using LiveCharts.Defaults;
using LiveCharts.Wpf;

namespace HSMClientWPFControls.View.SensorDialog
{
    /// <summary>
    /// Interaction logic for BarSensorView.xaml
    /// </summary>
    public partial class BarSensorView : SensorControl
    {
        public SeriesCollection Series;
        private BarSensorViewModel _vm;
        public BarSensorView()
        {
            InitializeComponent();
            //BoxPlotSeries.BoxWidth = 0.00000005;
            //BoxPlotSeries.Width = 0.0005;
        }

        private void ViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "Points" || e.PropertyName == "Labels")
            {
                UpdateView();
            }
        }

        private void UpdateView()
        {
            if (_vm == null)
                return;

            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(UpdateView);
                return;
            }

            Series = new SeriesCollection()
            {
                new CandleSeries
                {
                    Values = new ChartValues<OhlcPoint>(_vm.Points)
                }
            };

            //Chart.Series = Series;
        }

        private void InitializeSeries(Collection<OhlcPoint> points)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(new Action<Collection<OhlcPoint>>(InitializeSeries), points);
                return;
            }

            Series = new SeriesCollection()
            {
                new CandleSeries
                {
                    Values = new ChartValues<OhlcPoint>(points)
                }
            };
        }
        public override DialogViewModel ConstructDefaultViewModel(ISensorDialogModel model)
        {
            var viewModel = new BarSensorViewModel(model);
            //_vm = viewModel;
            //_vm.PropertyChanged += ViewModel_PropertyChanged;
            return viewModel;
        }
    }
}
