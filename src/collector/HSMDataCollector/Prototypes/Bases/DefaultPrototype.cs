using HSMDataCollector.Options;
using System.Linq;

namespace HSMDataCollector.Prototypes
{
    internal static class DefaultPrototype
    {
        private const string ComputerFolder = ".computer";
        private const string ModuleFolder = ".module";
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