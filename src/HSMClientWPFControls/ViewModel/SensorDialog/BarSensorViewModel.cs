using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using HSMClientWPFControls.Model.SensorDialog;
using OxyPlot.Series;

namespace HSMClientWPFControls.ViewModel.SensorDialog
{
    public class BarSensorViewModel : DialogViewModel
    {
        double _maxValue;
        double _minValue;
        public BarSensorViewModel(ISensorDialogModel model) : base(model)
        {
        }

        public double BoxWidth => double.MinValue;
        public double MinValue
        {
            get => _minValue - 5;
            set
            {
                _minValue = value;
                OnPropertyChanged(nameof(MinValue));
            }
        }

        public double MaxValue
        {
            get => _maxValue + 5;
            set
            {
                _maxValue = value;
                OnPropertyChanged(nameof(MaxValue));
            }
        }
        public Collection<BoxPlotItem> Items
        {
            get
            {
                var model = Model as IBarSensorModel;
                double minv = Double.MaxValue;
                double maxv = Double.MinValue;
                foreach (var it in model.Items)
                {
                    minv = Math.Min(minv, it.LowerWhisker);
                    maxv = Math.Max(maxv, it.UpperWhisker);
                }
                MaxValue = maxv;
                MinValue = minv;
                return model?.Items;
            }
            set
            {
                var model = Model as IBarSensorModel;
                if (model != null)
                {
                    model.Items = value;
                    double minv = Double.MaxValue;
                    double maxv = Double.MinValue;
                    foreach (var it in model.Items)
                    {
                        minv = Math.Min(minv, it.LowerWhisker);
                        maxv = Math.Max(maxv, it.UpperWhisker);
                    }
                    MaxValue = maxv;
                    MinValue = minv;
                }
                OnPropertyChanged(nameof(Items));
            }
        }
        public string Count
        {
            get
            {
                var model = Model as IBarSensorModel;
                return model?.Count.ToString();
            }
            set
            {
                var model = Model as IBarSensorModel;
                if (model != null)
                {
                    model.Count = int.Parse(value);
                }
                OnPropertyChanged();
                OnPropertyChanged(nameof(Count));
            }
        }
    }
}
