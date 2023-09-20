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


    public Task<bool> Connect(string country) => GetResult(string.Format(ConnectCommand).BashExecute());

    public Task<bool> Disconnect() => GetResult(DisconnectCommand.BashExecute());

    public Task<bool> ChangeCountry(string country, out string result)
    {
        result = string.Empty;

        if (_countries.TryGetValue(country, out _))
            return Connect(country);

        return Task.FromResult(false);
    }

    private static async Task<bool> GetResult(string result)
    {
        //message = result;

        if (result.StartsWith(ErrorStarted))
            return false;

        await Task.Delay(TimeSpan.FromSeconds(10));

        return true;
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