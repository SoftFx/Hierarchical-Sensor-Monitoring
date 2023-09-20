using HSMPingModule.Common;

namespace HSMPingModule.VpnManager
{
    internal sealed class NoVpnManager : BaseVpnManager
    {
        protected override Task<List<string>> GetAvailableCountries() => Task.FromResult<List<string>>(new()
        {
            "Latvia",
            "United_Kingdom"
        });


        internal override Task<TaskResult> Connect() => TaskResult.OkTask;

        internal override Task<TaskResult> Disconnect() => TaskResult.OkTask;
    }
}