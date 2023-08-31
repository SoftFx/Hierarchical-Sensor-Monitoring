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
        if (!Directory.Exists(ConfigPath))
        {
            logger.Log(new LogEventInfo(LogLevel.Debug, "qwe", "no config found"));
            FileManager.SafeCreateDirectory(ConfigPath);
        }

        _logger = logger;
        logger.Log(new LogEventInfo(LogLevel.Debug, "qwe", $"{Directory.Exists(ConfigPath)}"));
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
            //log exception
        }
    }


    private T Read<T>(string sectionName) where T : class, new() => _configuration.GetSection(sectionName).Get<T>() ?? new T();

    private void Read()
    {
        CollectorSettings = Read<CollectorSettings>(nameof(CollectorSettings));
        ResourceSettings = Read<ResourceSettings>(nameof(ResourceSettings)).ApplyDefaultSettings();
        _logger.Log(new LogEventInfo(LogLevel.Debug, "qwe", $"{CollectorSettings.Key}"));
        _logger.Log(new LogEventInfo(LogLevel.Debug, "qwe", $"{CollectorSettings.Port}"));
        _logger.Log(new LogEventInfo(LogLevel.Debug, "qwe", $"{CollectorSettings.ServerAddress}"));

        OnChange?.Invoke();
    }
}