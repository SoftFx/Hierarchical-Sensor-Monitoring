using HSMPingModule.Common;

namespace HSMPingModule.VpnManager
{
    internal sealed class NoVpnManager : BaseVpnManager
    {
        internal override string VpnDescription { get; } = $"Test start without using any VPN";


        protected override Task<TaskResult<List<string>>> LoadAvailableCountries() =>
            Task.FromResult(TaskResult<List<string>>.GetOk(new()
            {
                "Latvia",
                "United_Kingdom"
            }));


        internal override Task<TaskResult<string>> Connect() => Task.FromResult(TaskResult<string>.GetOk(string.Empty));

        internal override Task<TaskResult> Disconnect() => TaskResult.OkTask;
    }
}