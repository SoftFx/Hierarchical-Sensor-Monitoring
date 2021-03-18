using System.Collections.ObjectModel;
using HSMClientWPFControls.Model.SensorDialog;
using OxyPlot;
using OxyPlot.Series;

namespace HSMClientWPFControls.ViewModel.SensorDialog
{
    public class BoolSensorViewModel : DialogViewModel
    {
        public BoolSensorViewModel(ISensorDialogModel model) : base(model)
        {
        }

        public ObservableCollection<string> Times
        {
            get
            {
                var model = Model as IBoolSensorModel;
                return model?.Times;
            }
            set
            {
                var model = Model as IBoolSensorModel;
                if (model != null)
                {
                    model.Times = value;
                }
                OnPropertyChanged(nameof(Times));
            }
        }
        public ObservableCollection<ColumnItem> Data
        {
            get
            {
                var model = Model as IBoolSensorModel;
                return model?.Data;
            }
            set
            {
                var model = Model as IBoolSensorModel;
                if (model != null)
                {
                    model.Data = value;
                }
                OnPropertyChanged(nameof(Data));
            }
        }

        public string Count
        {
            get
            {
                var model = Model as IBoolSensorModel;
                return model?.Count.ToString();
            }
            set
            {
                var model = Model as IBoolSensorModel;
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
