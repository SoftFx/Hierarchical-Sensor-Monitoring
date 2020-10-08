using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using HSMServer.Model;

namespace HSMServer.MonitoringServerCore
{
    public interface IMonitoringCore
    {
        public void AddSensorInfo(JobResult info);
        public void AddSensorInfo(NewJobResult info);
        public void GetSensorUpdates(X509Certificate2 clientCertificate);
        public void GetSensorsTree(X509Certificate2 clientCertificate);
    }
}
