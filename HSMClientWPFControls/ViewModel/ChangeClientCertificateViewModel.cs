using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Input;
using HSMClientWPFControls.Bases;
using HSMClientWPFControls.Model;

namespace HSMClientWPFControls.ViewModel
{
    public class ChangeClientCertificateViewModel : ViewModelBase
    {
        private readonly IMonitoringModel _monitoringModel;
        public ChangeClientCertificateViewModel(IMonitoringModel monitoringModel)
        {
            _monitoringModel = monitoringModel;
            ShowGenerateCertificateWindowCommand = new MultipleDelegateCommand(ShowGenerateCertificateWindow,
                CanShowGenerateCertificateWindow);
        }

        public ICommand ShowGenerateCertificateWindowCommand { get; private set; }

        private void ShowGenerateCertificateWindow()
        {
            _monitoringModel.ShowGenerateCertificateWindow();
        }

        private bool CanShowGenerateCertificateWindow()
        {
            return true;
        }
    }
}
