using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
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

    public CollectorSettings HSMDataCollectorSettings { get; private set; } = new();

    public ResourceSettings ResourceSettings { get; private set; } = new();


    static ServiceConfig()
    {
        var assembly = Assembly.GetExecutingAssembly().GetName();

        Version = assembly.Version;

        if (!Directory.Exists(ConfigPath))
            Directory.CreateDirectory(ConfigPath);
    }


    public void SetUpConfig(IConfigurationRoot configuration, NLog.ILogger logger)
    {
        _logger = logger;
        _configuration = configuration;

        if (!File.Exists(_settingsPath))
        {
            using var file = File.Open(_settingsPath, FileMode.Create);
            file.Write( JsonSerializer.SerializeToUtf8Bytes(this, _options));
        }
        else 
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
        HSMDataCollectorSettings = Read<CollectorSettings>(nameof(HSMDataCollectorSettings));
        ResourceSettings = Read<ResourceSettings>(nameof(ResourceSettings)).ApplyDefaultSettings();

        _logger.Info("Read collector key: {key}", HSMDataCollectorSettings.Key);
        _logger.Info("Read collector port: {port}", HSMDataCollectorSettings.Port);
        _logger.Info("Read server address: {adress}", HSMDataCollectorSettings.ServerAddress);

        OnChange?.Invoke();
    }
}