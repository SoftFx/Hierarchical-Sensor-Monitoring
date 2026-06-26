using HSMServer.ServerConfiguration;
using System.ComponentModel.DataAnnotations;

namespace HSMServer.Model.Configuration
{
    public class AgentSettingsViewModel
    {
        [Display(Name = "Agent connection URL")]
        public string ExternalConnectionUrl { get; set; }

        [Display(Name = "Allow untrusted server certificate")]
        public bool AllowUntrustedCertificate { get; set; }

        [Display(Name = "Report top processes by CPU")]
        public bool EnableTopCpuProcesses { get; set; }

        public AgentSettingsViewModel() { }

        public AgentSettingsViewModel(IServerConfig config)
        {
            ExternalConnectionUrl = config.Agent.ExternalConnectionUrl;
            AllowUntrustedCertificate = config.Agent.AllowUntrustedCertificate;
            EnableTopCpuProcesses = config.Agent.EnableTopCpuProcesses;
        }
    }
}
