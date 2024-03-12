using HSMServer.Core;
using HSMServer.Core.Model;
using HSMServer.Extensions;
using HSMServer.Model.Authentication;
using HSMServer.Model.TreeViewModel;
using System.Linq;

namespace HSMServer.Model.UserTreeShallowCopy
{
    public sealed class SensorShallowModel : BaseNodeShallowModel<SensorNodeViewModel>
    {
        public override bool IsGrafanaEnabled { get; }

        public override bool HasUnconfiguredAlerts { get; }


        internal SensorShallowModel(SensorNodeViewModel data, User user) : base(data, user)
        {
            IsGrafanaEnabled = data.Integration.HasGrafana();
            HasUnconfiguredAlerts = data.DataAlerts.Values.Any(d => d.Any(a => a.IsUnconfigured())) || data.TTLAlert.IsUnconfigured();

            if (data.Status is TreeViewModel.SensorStatus.Error)
                ErrorsCount = 1;

            _mutedValue = data.State == SensorState.Muted;
        }
    }
}