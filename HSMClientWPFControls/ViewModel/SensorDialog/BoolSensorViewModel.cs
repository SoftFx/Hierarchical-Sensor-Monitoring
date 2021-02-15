using System.Collections.ObjectModel;
using HSMClientWPFControls.Model.SensorDialog;
using OxyPlot;

namespace HSMClientWPFControls.ViewModel.SensorDialog
{
    public class BoolSensorViewModel : DialogViewModel
    {
        public BoolSensorViewModel(ISensorDialogModel model) : base(model)
        {
        }

        public ObservableCollection<DataPoint> Data
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

        public string CountText
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
                OnPropertyChanged(nameof(CountText));
            }
        }
    }
}
