using HSMServer.Core;
using HSMServer.Core.Model;
using HSMServer.Extensions;
using HSMServer.Model.Authentication;
using HSMServer.Model.TreeViewModel;

namespace HSMServer.Model.UserTreeShallowCopy
{
    public sealed class SensorShallowModel : BaseNodeShallowModel<SensorNodeViewModel>
    {
        public override bool IsGrafanaEnabled { get; }

        public override bool HasUnconfiguredAlerts { get; }


        internal SensorShallowModel(SensorNodeViewModel data, User user) : base(data, user)
        {
            IsGrafanaEnabled = data.Integration.HasGrafana();
            HasUnconfiguredAlerts = data.HasUnconfiguredAlerts();

            if (data.Status is TreeViewModel.SensorStatus.Error)
                ErrorsCount = 1;

            _mutedValue = data.State == SensorState.Muted;
        }
    }
}