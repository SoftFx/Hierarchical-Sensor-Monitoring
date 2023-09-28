using HSMPingModule.Common;
using HSMPingModule.Console;
using NLog;

namespace HSMPingModule.VpnManager
{
    internal sealed class NordVpnManager : BaseVpnManager
    {
        private const int MaxAttemptsCnt = 5;

        private const string ServiceName = "nordvpn";
        private const string ErrorAnswer = "Whoops!";

        private const string CountriesListCommand = "countries";
        private const string DisconnectCommand = "disconnect";
        private const string ConnectCommand = "connect";

        private static readonly HashSet<string> _skipOutputLines = new()
        {
            "/", "|", "-", "\\", "A new version of NordVPN is available! Please update the application."
        };

        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        private string _description = $"[**Nord VPN**](https://nordvpn.com/) is used to check configured resources.";


        internal override string VpnDescription => _description;


        internal override async Task<TaskResult> Connect()
        {
            var result = await RepeatingRun(ConnectCommand);

            return result.IsOk ? TaskResult.Ok : new TaskResult(result.Error);
        }

        internal override async Task<TaskResult> Disconnect()
        {
            var result = await RunCommand(DisconnectCommand);

            return result.IsOk ? TaskResult.Ok : new TaskResult(result.Error);
        }


        internal override async Task<TaskResult> SwitchCountry(string country)
        {
            var check = await base.SwitchCountry(country);

            if (!check.IsOk)
                return check;

            var result = await RepeatingRun($"{ConnectCommand} {country}");

            return result.IsOk ? TaskResult.Ok : new TaskResult($"Cannot connect to country {country}. {result.Error}");
        }


        protected override async Task<TaskResult<List<string>>> LoadAvailableCountries()
        {
            var result = await RunCommand(CountriesListCommand);

            if (result.IsOk)
            {
                _description = $"{_description}  \nAvailable countires:  \n{result.Result}";

                var countries = result.Result.Split(",", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

                return TaskResult<List<string>>.GetOk(countries);
            }

            return new TaskResult<List<string>>(result.Error);
        }


        private static async Task<TaskResult<string>> RepeatingRun(string command)
        {
            int attempt = 0;

            do
            {
                _logger.Info($"Command: {command}, attempt = {++attempt}");

                var result = await RunCommand(command);

                if (result.IsOk || attempt == MaxAttemptsCnt)
                    return result;
            }
            while (attempt <= MaxAttemptsCnt);

            return new TaskResult<string>("NordVpn not answer");
        }

        private static async Task<TaskResult<string>> RunCommand(string command)
        {
            var utc = DateTime.UtcNow;

            var result = await ConsoleExecutor.Run($"{ServiceName} {command}", _skipOutputLines);

            _logger.Info($"Command duration: {(DateTime.UtcNow - utc).TotalSeconds} s");

            return result.Contains(ErrorAnswer) || string.IsNullOrEmpty(result) ? new TaskResult<string>(result) : TaskResult<string>.GetOk(result);
        }
    }
}