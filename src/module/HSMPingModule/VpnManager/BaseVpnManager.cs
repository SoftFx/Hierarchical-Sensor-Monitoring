using HSMPingModule.Common;

namespace HSMPingModule.VpnManager
{
    internal abstract class BaseVpnManager
    {
        internal HashSet<string> AvailableCountries { get; private set; }


        internal virtual async Task Init()
        {
            AvailableCountries = (await GetAvailableCountries()).ToHashSet();
        }


        internal abstract Task<TaskResult> Connect();

        internal abstract Task<TaskResult> Disconnect();


        internal virtual Task<TaskResult> SwitchCountry(string country) => AvailableCountries.Contains(country)
            ? Task.FromResult(TaskResult.Ok)
            : Task.FromResult(new TaskResult($"Country is not available {country}"));


        protected abstract Task<List<string>> GetAvailableCountries();
    }
}
