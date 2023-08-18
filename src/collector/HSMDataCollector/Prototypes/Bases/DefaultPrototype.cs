using HSMDataCollector.Options;
using System.Linq;

namespace HSMDataCollector.Prototypes
{
    internal static class DefaultPrototype
    {
        private const string PathSeparator = "/";
        private const string SystemPath = ".Default";


        internal static T Merge<T>(T defaultOptions, T customOptions) where T : SensorOptions, new() =>
            new T()
            {
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
                AggregateData = customOptions?.AggregateData ?? defaultOptions.AggregateData,
            };


        internal static string BuildDefaultPath(string category, string path) =>
            BuildPath(new string[]
            {
                SystemPath,
                category,
                path,
            });

        internal static string BuildPath(params string[] parts) => string.Join(PathSeparator, parts.Where(u => !string.IsNullOrEmpty(u)));
    }
}