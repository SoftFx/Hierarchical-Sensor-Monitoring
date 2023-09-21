using HSMPingModule.Settings;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HSMPingModule.Config;

internal sealed class ServiceConfig
{
#if RELEASE
    public const string ConfigName = "appsettings.json";
#else
    public const string ConfigName = "appsettings.Development.json";
#endif

    private static readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
    };

    private readonly string _fullFilePath = Path.Combine(ConfigPath, ConfigName);
    private readonly FileSystemWatcher _watcher;
    private readonly IConfigurationRoot _root;
    private readonly NLog.ILogger _logger;

    private bool _reloadIsFired;


    [JsonIgnore]
    internal static string ConfigPath { get; } = Path.Combine(Environment.CurrentDirectory, "Config");

    [JsonIgnore]
    internal static Version Version { get; }


    public CollectorSettings HSMDataCollectorSettings { get; private set; } = new();

    public ResourceSettings ResourceSettings { get; private set; } = new();

    public PingSettings PingSettings { get; private set; } = new();


    internal event Action OnChanged;


    static ServiceConfig()
    {
        var assembly = Assembly.GetExecutingAssembly().GetName();

        Version = assembly.Version;

        if (!Directory.Exists(ConfigPath))
            Directory.CreateDirectory(ConfigPath);
    }

    internal ServiceConfig(IConfigurationRoot configuration, NLog.ILogger logger)
    {
        _root = configuration;
        _logger = logger;

        if (!File.Exists(_fullFilePath))
            SaveDefaultConfig();

        Init();

        _watcher = new FileSystemWatcher(_fullFilePath)
        {
            NotifyFilter = NotifyFilters.LastWrite,
            EnableRaisingEvents = true
        };

        _watcher.Changed += Reload;
    }


    private void Init()
    {
        T Read<T>(string sectionName) where T : class, new() => _root.GetSection(sectionName).Get<T>() ?? new T();

        HSMDataCollectorSettings = Read<CollectorSettings>(nameof(HSMDataCollectorSettings));
        ResourceSettings = Read<ResourceSettings>(nameof(ResourceSettings)).ApplyDefaultSettings();
        PingSettings = Read<PingSettings>(nameof(PingSettings));

        _logger.Info("Read collector key: {key}", HSMDataCollectorSettings.Key);
        _logger.Info("Read collector port: {port}", HSMDataCollectorSettings.Port);
        _logger.Info("Read server address: {adress}", HSMDataCollectorSettings.ServerAddress);

        OnChanged?.Invoke();
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

    private void SaveDefaultConfig()
    {
        using var file = File.Open(_fullFilePath, FileMode.Create);
        file.Write(JsonSerializer.SerializeToUtf8Bytes(this, _options));
    }
}