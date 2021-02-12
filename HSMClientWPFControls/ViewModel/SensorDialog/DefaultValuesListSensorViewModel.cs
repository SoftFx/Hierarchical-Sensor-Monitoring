using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using System.Windows.Input;
using HSMClientWPFControls.Model;
using HSMClientWPFControls.Model.SensorDialog;

namespace HSMClientWPFControls.ViewModel.SensorDialog
{
    public class DefaultValuesListSensorViewModel : DialogViewModel
    {
        private int _amount;
        public DefaultValuesListSensorViewModel(ISensorDialogModel model) : base(model)
        {
            RefreshCommand = new MultipleDelegateCommand(Refresh, CanRefresh);
        }
        public ICommand RefreshCommand { get; private set; }

        public string AmountText
        {
            get => _amount.ToString();
            set
            {
                _amount = int.Parse(value);
                OnPropertyChanged(nameof(AmountText));
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
                if (Model == null)
                {
                    var model = Model as IDefaultValuesListModel;
                    if (model == null)
                        model.List = value;
                }
                OnPropertyChanged(nameof(List));
            }
        }

        public string CountText
        {
            get
            {
                var model = Model as IDefaultValuesListModel;
                return model?.CountText;
            }
            set
            {
                if (Model == null)
                {
                    var model = Model as IDefaultValuesListModel;
                    if (model == null)
                        model.CountText = value;
                }
                OnPropertyChanged(nameof(CountText));
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
