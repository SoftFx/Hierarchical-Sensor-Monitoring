namespace HSMServer.Core.Model.Policies.ServerPolicies
{
    public sealed class SelfDestroyPolicy : ServerPolicy
    {
        protected override SensorStatus FailStatus => SensorStatus.Error;

        protected override string FailMessage => string.Empty;
    }
}