using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using HSMCommon;
using HSMPingModule.Settings;

namespace HSMPingModule.Config;

internal sealed class ServiceConfig
{
    private static readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
    };

    private readonly string _settingsPath = Path.Combine(ConfigPath, ConfigName);

    private IConfigurationRoot _configuration;


#if RELEASE
        public const string ConfigName = "appsettings.json";
#else
    public const string ConfigName = "appsettings.Development.json";
#endif

    [JsonIgnore] 
    internal static string ConfigPath { get; } = Path.Combine(Environment.CurrentDirectory, "Config");


    [JsonIgnore] 
    internal static Version Version { get; }


    public CollectorSettings CollectorSettings { get; private set; }

    public ResourceSettings ResourceSettings { get; private set; }


    static ServiceConfig()
    {
        var assembly = Assembly.GetExecutingAssembly().GetName();

        Version = assembly.Version;

        if (!Directory.Exists(ConfigPath))
            FileManager.SafeCreateDirectory(ConfigPath);
    }


    public void SetUpConfig(IConfigurationRoot configuration)
    {
        _configuration = configuration;

        CollectorSettings = Register<CollectorSettings>(nameof(CollectorSettings));
        ResourceSettings = Register<ResourceSettings>(nameof(ResourceSettings));
    }


    private T Register<T>(string sectionName) where T : class, new() => _configuration.GetSection(sectionName).Get<T>() ?? new T();

    public void Reload()
    {
        _configuration.Reload();
        CollectorSettings = Register<CollectorSettings>(nameof(CollectorSettings));
        ResourceSettings = Register<ResourceSettings>(nameof(ResourceSettings));
    }
}