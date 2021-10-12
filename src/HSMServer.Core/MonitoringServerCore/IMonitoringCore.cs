using HSMCommon.Model;
using HSMServer.Core.Model.Authentication;
using System;
using System.Security.Cryptography.X509Certificates;
using RSAParameters = System.Security.Cryptography.RSAParameters;

namespace HSMServer.Core.MonitoringServerCore
{
    public interface IMonitoringCore : IDisposable
    {
        //Task<bool> AddSensorValueAsync(BoolSensorValue value);
        (X509Certificate2, X509Certificate2) SignClientCertificate(User user, string subject, string commonName,
            RSAParameters rsaParameters);
        ClientVersionModel GetLastAvailableClientVersion();
    }
}
