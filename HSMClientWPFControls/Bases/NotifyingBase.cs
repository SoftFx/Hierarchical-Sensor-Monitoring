using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace HSMClientWPFControls.Bases
{
    public class NotifyingBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
