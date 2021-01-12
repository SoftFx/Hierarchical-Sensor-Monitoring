using System;
using System.Windows.Input;
using HSMClient.Common;
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
            _monitoringModel.ConnectionStatusChanged += monitoringModel_ConnectionStatusChanged;
        }

        private void monitoringModel_ConnectionStatusChanged(object sender, EventArgs e)
        {
            OnPropertyChanged(nameof(ConnectionStatusText));
            OnPropertyChanged(nameof(IsGenerateEnabled));
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

        public string ConnectionStatusText
        {
            get => _monitoringModel.IsConnected ? 
                TextConstants.SuccessfulConnectionText 
                : TextConstants.BadConnectionText;
        }

        public bool IsGenerateEnabled => _monitoringModel.IsConnected;

        public string ServerAddressText => $"Current server address: '{_monitoringModel.ConnectionAddress}'";
    }
}
