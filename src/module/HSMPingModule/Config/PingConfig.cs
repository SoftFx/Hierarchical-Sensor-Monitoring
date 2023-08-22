using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using HSMCommon;
using HSMPingModule.Settings;
using HSMServer.Extensions;

namespace HSMPingModule.Config;

internal sealed class PingConfig
{
    private static readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
    };

    private readonly string _settingsPath = Path.Combine(ConfigPath, ConfigName);

    private readonly IConfigurationRoot _configuration;


#if RELEASE
        public const string ConfigName = "appsettings.json";
#else
    public const string ConfigName = "appsettings.Development.json";
#endif

    [JsonIgnore] 
    public static string ConfigPath { get; } = Path.Combine(Environment.CurrentDirectory, "Config");

    [JsonIgnore] 
    public static string ExecutableDirectory { get; }


    [JsonIgnore] 
    public static string Version { get; }

    [JsonIgnore] 
    public static string Name { get; }


    public CollectorSettings CollectorSettings { get; }

    public VpnSettings VpnSettings { get; }



    static PingConfig()
    {
        var assembly = Assembly.GetExecutingAssembly().GetName();

        Name = assembly.Name;
        ExecutableDirectory = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);

        Version = assembly.GetVersion().RemoveTailZeroes();

        if (!Directory.Exists(ConfigPath))
            FileManager.SafeCreateDirectory(ConfigPath);
    }

    public PingConfig(IConfigurationRoot configuration)
    {
        _configuration = configuration;

        CollectorSettings = Register<CollectorSettings>(nameof(CollectorSettings));
        VpnSettings = Register<VpnSettings>(nameof(VpnSettings));

        ResaveSettings();
    }

    public void ResaveSettings() => File.WriteAllText(_settingsPath, JsonSerializer.Serialize(this, _options));


    private T Register<T>(string sectionName) where T : class, new()
    {
        return _configuration.GetSection(sectionName).Get<T>() ?? new T();
    }
}