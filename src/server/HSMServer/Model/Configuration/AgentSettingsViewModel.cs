using HSMServer.ServerConfiguration;
using System.ComponentModel.DataAnnotations;

namespace HSMServer.Model.Configuration
{
    public class AgentSettingsViewModel
    {
        [Display(Name = "Agent connection URL")]
        public string ExternalConnectionUrl { get; set; }

        public AgentSettingsViewModel() { }

        public AgentSettingsViewModel(IServerConfig config)
        {
            ExternalConnectionUrl = config.Agent.ExternalConnectionUrl;
        }
    }
}
