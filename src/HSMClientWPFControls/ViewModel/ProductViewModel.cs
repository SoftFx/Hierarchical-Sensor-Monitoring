using System;
using HSMClientWPFControls.Bases;
using HSMClientWPFControls.Objects;

namespace HSMClientWPFControls.ViewModel
{
    public class ProductViewModel : NotifyingBase
    {
        public ProductViewModel(ProductInfo info)
        {
            Info = info;
        }
        public ProductInfo Info { get; private set; }
        public string Name
        {
            get => Info.Name;
            set
            {
                //Info.Name = value;
                OnPropertyChanged(nameof(Name));
            }
        }

        public string Key
        {
            get => Info.Key;
            //set;
        }

        public string DateAdded
        {
            get => Info.DateRegistered.ToString("F");
            //set;
        }
    }
}
