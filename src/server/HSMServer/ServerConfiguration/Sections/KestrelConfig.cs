using HSMServer.Middleware;
using Microsoft.Extensions.Configuration;
using System;
using System.Linq;
using System.Text.Json.Serialization;

namespace HSMServer.ServerConfiguration;

public class KestrelConfig
{
    public const int DefaultSensorPort = 44330;
    public const int DefaultSitePort = 44333;

    public const string TrustedProxiesKey = "Kestrel:" + nameof(TrustedProxies);


    public int SensorPort { get; set; } = DefaultSensorPort;

    public int SitePort { get; set; } = DefaultSitePort;

    // Reverse proxies whose X-Forwarded-For is trusted for the client address (#1427); entry
    // syntax in TrustedProxyOptionsFactory. Empty = no forwarded headers are honoured, so a
    // server reached directly keeps using the connection address.
    // Read from the environment only (ReadTrustedProxies; docker-compose.yml sets it):
    // ServerConfig rewrites appsettings.json on every start, so a hand-written file value
    // could not survive the resave that [JsonIgnore] has to keep from persisting the
    // environment's copy (a persisted copy would override the environment later, since the
    // settings file is registered after it). The default must stay an empty array: the
    // configuration binder appends to a pre-filled one.
    [JsonIgnore]
    public string[] TrustedProxies { get; set; } = [];


    // Accepts the indexed form (Kestrel__TrustedProxies__0=...) and a comma-separated scalar
    // (Kestrel__TrustedProxies=a,b). Blank entries are dropped, so an empty value switches the
    // trust off.
    public static string[] ReadTrustedProxies(IConfiguration environment)
    {
        var section = environment.GetSection(TrustedProxiesKey);
        var entries = section.Get<string[]>() ?? [];

        if (entries.Length == 0 && !string.IsNullOrWhiteSpace(section.Value))
            entries = section.Value.Split(',');

        return entries.Select(entry => entry?.Trim()).Where(entry => !string.IsNullOrEmpty(entry)).ToArray();
    }

    // The ignored settings-file value, for the startup error (null when there is none).
    public static string FindTrustedProxiesInSettingsFile(IConfigurationRoot configuration)
    {
        foreach (var provider in configuration.Providers.OfType<FileConfigurationProvider>())
        {
            var values = provider.GetChildKeys([], TrustedProxiesKey).Distinct()
                .Select(child => provider.TryGet($"{TrustedProxiesKey}:{child}", out var value) ? value : null)
                .ToList();

            if (provider.TryGet(TrustedProxiesKey, out var scalar) && !string.IsNullOrWhiteSpace(scalar))
                values.Add(scalar);

            if (values.Count > 0)
                return string.Join(", ", values);
        }

        return null;
    }


    public void Validate()
    {
        foreach (var entry in TrustedProxies)
            if (!TrustedProxyOptionsFactory.IsValidEntry(entry))
                throw new InvalidOperationException(
                    $"Kestrel.{nameof(TrustedProxies)} contains '{entry}', which is neither an IP address, a CIDR network (e.g. 172.18.0.0/16) nor '{TrustedProxyOptionsFactory.AttachedNetworksKeyword}'.");
    }
}
