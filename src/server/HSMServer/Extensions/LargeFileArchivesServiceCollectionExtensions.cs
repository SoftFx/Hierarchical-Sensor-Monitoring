using HSMServer.BackgroundServices;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Cache;
using HSMServer.Core.Journal;
using HSMServer.Core.LargeFiles.Archives;
using HSMServer.Core.LargeFiles.DumpPasswords;
using HSMServer.Core.LargeFiles.Downloads;
using HSMServer.Model.ManagementApi.LargeFiles;
using HSMServer.ServerConfiguration;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace HSMServer.ServiceExtensions
{
    /// <summary>The archive side of large files (ADR-0022 points 13-15, #1885) in one registration.</summary>
    public static class LargeFileArchivesServiceCollectionExtensions
    {
        public static IServiceCollection AddLargeFileArchives(this IServiceCollection services, IServerConfig config)
        {
            services.AddSingleton(config.LargeFileArchives ?? new LargeFileArchivesConfig());

            // PLACEHOLDER until #1883 lands: a store with no files. Replaced by the adapter over the real store then.
            services.AddSingleton<ILargeFileArchiveStore, UnavailableLargeFileArchiveStore>();

            // The self-monitoring sensors live inside the server's collector wrapper, like the other self-monitoring nodes.
            services.AddSingleton<ILargeFilesMonitor>(provider => provider.GetRequiredService<DataCollectorWrapper>().LargeFileSensors);

            var archives = config.LargeFileArchives ?? new LargeFileArchivesConfig();

            services.AddSingleton<IArchiveWriter, SharpZipArchiveWriter>();

            // The service reads the store, never the builder: the builder depends on the service.
            services.AddSingleton<IDumpPasswordService>(provider =>
            {
                var store = provider.GetRequiredService<ILargeFileArchiveStore>();

                return new DumpPasswordService(
                    provider.GetRequiredService<IDatabaseCore>(), provider.GetRequiredService<IJournalService>(),
                    provider.GetRequiredService<ITreeValuesCache>(), archives.PasswordRetention,
                    productId => HighestCarriedVersion(store, productId));
            });

            services.AddSingleton<IDownloadLog>(provider => new DownloadLog(
                provider.GetRequiredService<IDatabaseCore>().DownloadLog,
                new DownloadLogOptions { Window = archives.DownloadLogWindow, MaxWindows = archives.DownloadLogMaxCallers }));

            services.AddSingleton<IArchiveBuilder>(provider => new ArchiveBuilder(
                provider.GetRequiredService<ILargeFileArchiveStore>(), provider.GetRequiredService<IArchiveWriter>(),
                provider.GetRequiredService<IDumpPasswordService>(), () => DateTime.UtcNow,
                new ArchiveBuilderOptions { DeflateLevel = archives.DeflateLevel }));

            // Stateless over singletons; scoped like the other management read services (controllers and MCP tools).
            services.AddScoped<LargeFileDownloadService>();
            services.AddScoped<HSMServer.Model.LargeFiles.DumpPasswordPageService>();

            services.AddHostedService<LargeFileArchiveBackgroundService>();

            return services;
        }


        // The highest dump password version a packed archive of the product carries; 0 when it has none.
        private static int HighestCarriedVersion(ILargeFileArchiveStore store, Guid productId)
        {
            var highest = 0;

            foreach (var file in store.GetFiles(LargeArchiveFileState.Packed))
                if (file.ProductId == productId && file.PasswordVersion is int version && version > highest)
                    highest = version;

            return highest;
        }
    }
}
