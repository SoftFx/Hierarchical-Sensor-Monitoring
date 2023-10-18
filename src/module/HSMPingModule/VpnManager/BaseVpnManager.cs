using HSMPingModule.Common;

namespace HSMPingModule.VpnManager
{
    internal abstract class BaseVpnManager
    {
        internal HashSet<string> AvailableCountries { get; private set; }

        internal abstract string VpnDescription { get; }


        internal abstract Task<TaskResult<string>> Connect();

        internal abstract Task<TaskResult> Disconnect();

        protected abstract Task<TaskResult<List<string>>> LoadAvailableCountries();


        internal virtual async Task<TaskResult<List<string>>> LoadCountries()
        {
            var result = await LoadAvailableCountries();

            if (result.IsOk)
                AvailableCountries = result.Result.ToHashSet();

            return result;
        }

        internal virtual Task<TaskResult> SwitchCountry(string country) => AvailableCountries.Contains(country)
            ? Task.FromResult(TaskResult.Ok)
            : Task.FromResult(new TaskResult($"Country is not available {country}"));
    }
}
