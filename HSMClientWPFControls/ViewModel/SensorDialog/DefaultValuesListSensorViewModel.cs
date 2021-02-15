using System.Collections.ObjectModel;
using System.Windows.Input;
using HSMClientWPFControls.Model;
using HSMClientWPFControls.Model.SensorDialog;

namespace HSMClientWPFControls.ViewModel.SensorDialog
{
    public class DefaultValuesListSensorViewModel : DialogViewModel
    {
        public DefaultValuesListSensorViewModel(ISensorDialogModel model) : base(model)
        {
            RefreshCommand = new MultipleDelegateCommand(Refresh, CanRefresh);
        }
        public ICommand RefreshCommand { get; private set; }

        public string CountText
        {
            get
            {
                var model = Model as IDefaultValuesListModel;
                return model?.Count.ToString();
            }
            set
            {
                var model = Model as IDefaultValuesListModel;
                if (model != null)
                    model.Count = int.Parse(value);
                OnPropertyChanged(nameof(CountText));
            }
        }
        public ObservableCollection<DefaultSensorModel> List
        {
            get
            {
                var model = Model as IDefaultValuesListModel;
                return model?.List;
            }
            set
            {
                var model = Model as IDefaultValuesListModel;
                if (model != null)
                    model.List = value;
                OnPropertyChanged(nameof(List));
            }
        }

        private void Refresh()
        {
            var model = Model as IDefaultValuesListModel;
            model?.Refresh();
        }
        private bool CanRefresh()
        {
            return true;
        }
    }
}
