using System.IO;
using System.Linq;
using System.Reflection;
using System.Web;
using HSMDataCollector.Options;


namespace HSMDataCollector.Prototypes
{
    internal static class DefaultPrototype
    {
        internal const string ComputerFolder = ".computer";
        internal const string ModuleFolder = ".module";
        private const string PathSeparator = "/";


        internal static T Merge<T>(SensorOptions defaultOptions, T customOptions) where T : SensorOptions, new() =>
            new T()
            {
                IsComputerSensor = defaultOptions.IsComputerSensor,
                ComputerName = defaultOptions.ComputerName,
                Module = defaultOptions.Module,
                Path = defaultOptions.Path,
                Type = defaultOptions.Type,

                TtlAlert = customOptions?.TtlAlert ?? defaultOptions.TtlAlert,

                Description = customOptions?.Description ?? defaultOptions.Description,
                SensorUnit = customOptions?.SensorUnit ?? defaultOptions.SensorUnit,

                KeepHistory = customOptions?.KeepHistory ?? defaultOptions.KeepHistory,
                SelfDestroy = customOptions?.SelfDestroy ?? defaultOptions.SelfDestroy,
                TTL = customOptions?.TTL ?? defaultOptions.TTL,

                EnableForGrafana = customOptions?.EnableForGrafana ?? defaultOptions.EnableForGrafana,

                IsSingletonSensor = customOptions?.IsSingletonSensor ?? defaultOptions.IsSingletonSensor,
                AggregateData = customOptions?.AggregateData ?? defaultOptions.AggregateData,
                Statistics = customOptions?.Statistics ?? defaultOptions.Statistics,
                DataProcessor = customOptions?.DataProcessor ?? defaultOptions.DataProcessor,

                SensorLocation = customOptions?.SensorLocation ?? defaultOptions.SensorLocation,
            };


        internal static string RevealDefaultPath(SensorOptions options, string category, string path) =>
            BuildPath(new string[]
            {
                options.IsComputerSensor ? ComputerFolder : ModuleFolder,
                category,
                path,
            });

        internal static string BuildPath(params string[] parts) => string.Join(PathSeparator, parts.Select(u => u?.Trim('/')).Where(u => !string.IsNullOrEmpty(u)));
    }
}