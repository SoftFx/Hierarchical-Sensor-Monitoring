using System.Security.Cryptography.X509Certificates;
using HSMServer.Model;

namespace HSMServer.MonitoringServerCore
{
    public interface IMonitoringCore
    {
        public void AddSensorInfo(JobResult info);
        //public string AddSensorInfo(NewJobResult info);
        public SensorsService.SensorsUpdateMessage GetSensorUpdates(X509Certificate2 clientCertificate);
        public SensorsService.SensorsUpdateMessage GetAllAvailableSensorsUpdates(X509Certificate2 clientCertificate);
    }
}
