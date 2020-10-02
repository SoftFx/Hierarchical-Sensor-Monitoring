using System;
using System.Collections.Generic;
using HSMClient.Configuration;

namespace MAMSClient.Configuration
{
    public class MachineInfo
    {
        public string ID { get; set; }
        public string Name { get; set; }

        public List<SensorMonitoringInfo> Sensors { get; set; }
        public AggrMonitoringInfo AggrMonitoringInfo { get; set; }
        public TTSMonitoringInfo TTSMonitoringInfo { get; set; }
        public CertificateInfo CertificateInfo { get; set; }

        public MachineInfo()
        {
            ID = DateTime.Now.Ticks.ToString();
        }
    }
}
