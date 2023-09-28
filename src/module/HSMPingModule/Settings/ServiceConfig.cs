using HSMPingModule.Settings;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HSMPingModule.Config;

internal sealed class ServiceConfig
{
#if RELEASE
    private static readonly string _environmentDirectory = Path.Combine(Environment.CurrentDirectory, Assembly.GetExecutingAssembly().GetName().Name);
    public const string ConfigName = "appsettings.json";
#else
    private static readonly string _environmentDirectory = Environment.CurrentDirectory;
    public const string ConfigName = "appsettings.Development.json";
#endif

    private static readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        Converters = { new WebSitesJsonConverter() }
    };

    private readonly string _fullFilePath = Path.Combine(ConfigPath, ConfigName);
    private readonly FileSystemWatcher _watcher;
    private readonly IConfigurationRoot _root;
    private readonly NLog.ILogger _logger;

    private bool _reloadIsFired;


    [JsonIgnore]
    internal static string ConfigPath { get; } = Path.Combine(_environmentDirectory, "Config");

    [JsonIgnore]
    internal static Version Version { get; }


    public CollectorSettings HSMDataCollectorSettings { get; set; } = new();

    public PingSettings PingSettings { get;  set; } = new();

    public ResourceSettings ResourceSettings { get; set; } = new();


    internal event Action OnChanged;


    static ServiceConfig()
    {
        var assembly = Assembly.GetExecutingAssembly().GetName();

        Version = assembly.Version;

        if (!Directory.Exists(ConfigPath))
            Directory.CreateDirectory(ConfigPath);
    }

    public ServiceConfig(){}

    internal ServiceConfig(IConfigurationRoot configuration, NLog.ILogger logger)
    {
        _root = configuration;
        _logger = logger;

        _watcher = new FileSystemWatcher(ConfigPath, ConfigName)
        {
            NotifyFilter = NotifyFilters.LastWrite,
            EnableRaisingEvents = true
        };

        _watcher.Changed += Reload;

        if (!File.Exists(_fullFilePath))
            SaveOrCreate();

        Init();
    }


    private void Init()
    {
        var deserializedConfig = JsonSerializer.Deserialize<ServiceConfig>(File.ReadAllText(_fullFilePath), _options);

        HSMDataCollectorSettings = deserializedConfig.HSMDataCollectorSettings;
        ResourceSettings = deserializedConfig.ResourceSettings.ApplyDefaultSettings();
        PingSettings = deserializedConfig.PingSettings;

        _logger.Info("Read collector key: {key}", HSMDataCollectorSettings.Key);
        _logger.Info("Read collector port: {port}", HSMDataCollectorSettings.Port);
        _logger.Info("Read server address: {adress}", HSMDataCollectorSettings.ServerAddress);

        OnChanged?.Invoke();
    
        SaveOrCreate();
    }

    private void Reload(object sender, FileSystemEventArgs e)
    {
        try
        {
            if (!_reloadIsFired)
            {
                _logger.Info("Reload settings starting...");

                _reloadIsFired = true;

                _root.Reload();
                Init();

                _logger.Info("Reload settings finished");
            }
        }
        catch (Exception exception)
        {
            _logger.Info("Reload exception: {message}", exception.Message);
        }
        finally
        {
            _reloadIsFired = false;
        }
    }

    private void SaveOrCreate()
    {
        _watcher.EnableRaisingEvents = false;
        using var file = File.Open(_fullFilePath, FileMode.OpenOrCreate);
        file.Write(JsonSerializer.SerializeToUtf8Bytes(this, _options));
        _watcher.EnableRaisingEvents = true;
    }
}