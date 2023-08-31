using System.Reflection;
using System.Text.Json.Serialization;
using HSMCommon;
using HSMPingModule.Settings;
using NLog;
using LogLevel = NLog.LogLevel;

namespace HSMPingModule.Config;

internal sealed class ServiceConfig
{
    private IConfigurationRoot _configuration;
    private Logger _logger;

#if RELEASE
        public const string ConfigName = "appsettings.json";
#else
    public const string ConfigName = "appsettings.Development.json";
#endif

    [JsonIgnore] 
    internal static string ConfigPath { get; } = Path.Combine(Environment.CurrentDirectory, "Config");


    [JsonIgnore] 
    internal static Version Version { get; }


    internal event Action OnChange;
    
    public CollectorSettings CollectorSettings { get; private set; }

    public ResourceSettings ResourceSettings { get; private set; }


    static ServiceConfig()
    {
        var assembly = Assembly.GetExecutingAssembly().GetName();

        Version = assembly.Version;

        if (!Directory.Exists(ConfigPath))
            FileManager.SafeCreateDirectory(ConfigPath);
    }


    public void SetUpConfig(IConfigurationRoot configuration, Logger logger)
    {
        _logger = logger;
        _configuration = configuration;
        Read();
    }

    public void Reload()
    {
        try
        {
            _configuration.Reload();
            Read();
        }
        catch(Exception exception)
        {
            _logger.Log(new LogEventInfo(LogLevel.Error, "Reload exception:", $"{exception.Message}"));
        }
    }


    private T Read<T>(string sectionName) where T : class, new() => _configuration.GetSection(sectionName).Get<T>() ?? new T();

    private void Read()
    {
        CollectorSettings = Read<CollectorSettings>(nameof(CollectorSettings));
        ResourceSettings = Read<ResourceSettings>(nameof(ResourceSettings)).ApplyDefaultSettings();
        _logger.Log(new LogEventInfo(LogLevel.Debug, "Collector key:", $"{CollectorSettings.Key}"));
        _logger.Log(new LogEventInfo(LogLevel.Debug, "Collector port:", $"{CollectorSettings.Port}"));
        _logger.Log(new LogEventInfo(LogLevel.Debug, "Server adress:", $"{CollectorSettings.ServerAddress}"));

        OnChange?.Invoke();
    }
}