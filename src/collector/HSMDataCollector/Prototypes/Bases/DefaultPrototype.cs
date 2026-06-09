using System;
using System.Linq;
using HSMDataCollector.Options;


namespace HSMDataCollector.Prototypes
{
    internal static class DefaultPrototype
    {
        internal const string ComputerFolder = ".computer";
        internal const string ModuleFolder = ".module";
        private const string PathSeparator = "/";


        internal static T Merge<T, TDisplayUnit>(SensorOptions<TDisplayUnit> defaultOptions, T customOptions) 
            where T : SensorOptions<TDisplayUnit>, new()
            where TDisplayUnit : struct, Enum
        {
            return new T()
            {
                IsComputerSensor = defaultOptions.IsComputerSensor,
                ComputerName = defaultOptions.ComputerName,
                Module = defaultOptions.Module,
                Path = defaultOptions.Path,
                Type = defaultOptions.Type,

                TtlAlerts = customOptions?.TtlAlerts ?? defaultOptions.TtlAlerts,

                Description = customOptions?.Description ?? defaultOptions.Description,
                SensorUnit = customOptions?.SensorUnit ?? defaultOptions.SensorUnit,

                DisplayUnit = customOptions?.DisplayUnit ?? defaultOptions.DisplayUnit,

                KeepHistory = customOptions?.KeepHistory ?? defaultOptions.KeepHistory,
                SelfDestroy = customOptions?.SelfDestroy ?? defaultOptions.SelfDestroy,
                TTLs = customOptions?.TTLs ?? defaultOptions.TTLs,

                EnableForGrafana = customOptions?.EnableForGrafana ?? defaultOptions.EnableForGrafana,

                IsSingletonSensor = customOptions?.IsSingletonSensor ?? defaultOptions.IsSingletonSensor,
                AggregateData = customOptions?.AggregateData ?? defaultOptions.AggregateData,
                Statistics = customOptions?.Statistics ?? defaultOptions.Statistics,
                DataProcessor = customOptions?.DataProcessor ?? defaultOptions.DataProcessor,

                SensorLocation = customOptions?.SensorLocation ?? defaultOptions.SensorLocation,
            };
        }


        internal static string RevealDefaultPath(SensorOptions options, string category, string path) =>
            BuildPath(new string[]
            {
                options.IsComputerSensor ? ComputerFolder : ModuleFolder,
                category,
                path,
            });

        internal static string BuildPath(params string[] parts) =>
            string.Join(
                PathSeparator,
                parts
                    .Where(u => u != null)
                    .SelectMany(u => u.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
                    .Where(u => !string.IsNullOrWhiteSpace(u)));
    }
}
