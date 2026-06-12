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

        /// <summary>
        /// Normalises a set of path parts into a single forward-slash-joined sensor path. Drops
        /// null / empty / whitespace parts, splits each non-null part on '/' so an input like
        /// <c>"a/b"</c> contributes two segments, and collapses any internal <c>//</c> (the
        /// previous <c>Trim('/')</c>-based implementation preserved interior <c>//</c>; the
        /// current behaviour treats the path as canonical separator-joined segments).
        /// Contract is locked down by <c>DefaultPrototypeBuildPathTests</c> (#1087 E).
        /// </summary>
        internal static string BuildPath(params string[] parts) =>
            string.Join(
                PathSeparator,
                parts
                    .Where(u => u != null)
                    .SelectMany(u => u.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
                    .Where(u => !string.IsNullOrWhiteSpace(u)));
    }
}
