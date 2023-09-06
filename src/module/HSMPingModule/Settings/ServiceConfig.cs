using System.Reflection;
using System.Text.Json.Serialization;
using HSMCommon;
using HSMPingModule.Settings;

namespace HSMPingModule.Config;

internal sealed class ServiceConfig
{
    private IConfigurationRoot _configuration;
    private NLog.ILogger _logger;

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


    public void SetUpConfig(IConfigurationRoot configuration, NLog.ILogger logger)
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
            _logger.Info("Reload exception: {message}", exception.Message);
        }
    }


    private T Read<T>(string sectionName) where T : class, new() => _configuration.GetSection(sectionName).Get<T>() ?? new T();

    private void Read()
    {
        CollectorSettings = Read<CollectorSettings>(nameof(CollectorSettings));
        ResourceSettings = Read<ResourceSettings>(nameof(ResourceSettings)).ApplyDefaultSettings();

        _logger.Info("Read collector key: {key}", CollectorSettings.Key);
        _logger.Info("Read collector port: {port}", CollectorSettings.Port);
        _logger.Info("Read server address: {adress}", CollectorSettings.ServerAddress);

        OnChange?.Invoke();
    }
}