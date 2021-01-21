using System;
using System.Collections.ObjectModel;
using System.Security.Cryptography.X509Certificates;
using HSMClientWPFControls.Model;
using HSMClientWPFControls.Objects;

namespace HSMClient.Connection
{
    public interface IMonitoringConnector
    {
        public ObservableCollection<MonitoringNodeBase> Nodes { get; }
        public DateTime LastUpdate { get; }
        public bool IsConnected { get; }
        public bool IsClientCertificateDefault { get; }
        public string ConnectionAddress { get; }

        public void ReplaceClientCertificate(X509Certificate2 clientCertificate);
        public void Stop();

        public X509Certificate2 GetSignedClientCertificate(CreateCertificateModel model,
            out X509Certificate2 caCertificate);

        public void Restart();
    }
}
