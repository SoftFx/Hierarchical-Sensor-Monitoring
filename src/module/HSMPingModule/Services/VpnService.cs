using System.Diagnostics;
using static HSMPingModule.Services.VpnCommands;

namespace HSMPingModule.Services;

public sealed class VpnService
{
    private readonly HashSet<string> _countries;


    public VpnService()
    {
        _countries = CountriesCommand.BashExecute().Split(", ").ToHashSet();
    }


    public Task<bool> Connect(string country, out string result) => GetResult(string.Format(ConnectCommand, country).BashExecute(), out result);

    public Task<bool> Disconnect(out string error) => GetResult(DisconnectCommand.BashExecute(), out error);

    public Task<bool> ChangeCountry(string country, out string result)
    {
        if (_countries.TryGetValue(country, out _))
            return Connect(country, out result);

        result = string.Empty;
        return Task.FromResult(false);
    }

    private static Task<bool> GetResult(string result, out string message)
    {
        message = result;
        return Task.FromResult(!result.StartsWith(ErrorStarted));
    }
}

public static class VpnCommands
{
    public const string ErrorStarted = "Whoops";
    public const string ConnectCommand = "nordvpn c {0}";
    public const string DisconnectCommand = "nordvpn d";
    public const string CountriesCommand = "nordvpn countries";


    internal static string BashExecute(this string command)
    {
        using var process = new Process();

        process.StartInfo = new ProcessStartInfo
        {
            FileName = "/bin/bash",
            Arguments = $"-c \"{command.Replace("\"", "\\\"")}\"",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        process.Start();

        var result = process.StandardOutput.ReadToEnd();

        process.WaitForExit();

        return result;
    }
}