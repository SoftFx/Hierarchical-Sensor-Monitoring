using HSMServer.BackgroundServices;
using HSMServer.Core.LargeFiles.Archives;
using HSMServer.Model.ManagementApi.LargeFiles;
using HSMServer.ServerConfiguration;
using Microsoft.Extensions.DependencyInjection;

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

            // ---- ENABLE WHEN MERGING #1885 -----------------------------------------------------------------------------
            // The implementations of the archive builder, the zip writer, the dump passwords and the download log are on
            // their own branches. Until they are merged, nothing below can be resolved, so it stays commented out: the web
            // and API download routes, the dump password section of the product settings and the MCP tool are inert (the
            // routes answer 500 on activation, the settings section is not shown), and no hosted service runs.
            //
            // services.AddSingleton<HSMServer.Core.LargeFiles.Archives.IArchiveWriter, HSMServer.Core.LargeFiles.Archives.SharpZipArchiveWriter>();
            // services.AddSingleton<HSMServer.Core.LargeFiles.DumpPasswords.IDumpPasswordService, HSMServer.Core.LargeFiles.DumpPasswords.DumpPasswordService>();
            // services.AddSingleton<HSMServer.Core.LargeFiles.Downloads.IDownloadLog, HSMServer.Core.LargeFiles.Downloads.DownloadLog>();
            // services.AddSingleton<HSMServer.Core.LargeFiles.Archives.IArchiveBuilder, HSMServer.Core.LargeFiles.Archives.ArchiveBuilder>();
            //
            // // Stateless over singletons; scoped like the other management read services (controllers and MCP tools).
            // services.AddScoped<LargeFileDownloadService>();
            // services.AddScoped<HSMServer.Model.LargeFiles.DumpPasswordPageService>();
            //
            // services.AddHostedService<LargeFileArchiveBackgroundService>();
            // ---- END ENABLE WHEN MERGING #1885 -------------------------------------------------------------------------

            return services;
        }
    }
}
