using HSMClientWPFControls.Bases;

namespace HSMClientWPFControls.ViewModel
{
    public class ProblemBaseViewModel : ViewModelBase
    {
        private string _name;
        private string _error;
        public ProblemBaseViewModel(string name, string error)
        {
            Name = name;
            Error = error;
        }

        public string Name
        {
            get => _name;
            set
            {
                _name = value;
                OnPropertyChanged(nameof(Name));
            }
        }

        public string Error
        {
            get => _error;
            set
            {
                _error = value;
                OnPropertyChanged(nameof(Error));
            }
        }
    }
}
