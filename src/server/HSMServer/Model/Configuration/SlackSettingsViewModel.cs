using HSMServer.Authentication;
using HSMServer.Model.Notifications;
using HSMServer.Notifications;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Model.Configuration
{
    public class SlackSettingsViewModel
    {
        public List<SlackDestinationViewModel> Destinations { get; }


        public SlackSettingsViewModel() { }

        public SlackSettingsViewModel(ISlackDestinationsManager destinations, IUserManager userManager = null)
        {
            Destinations = destinations.GetValues()
                .Select(d => new SlackDestinationViewModel(d, userManager))
                .ToList();
        }
    }
}
