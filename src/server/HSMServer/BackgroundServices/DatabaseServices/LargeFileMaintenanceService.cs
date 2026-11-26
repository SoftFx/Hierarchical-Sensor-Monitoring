using System;
using System.Threading;
using System.Threading.Tasks;
using HSMServer.Core.LargeFiles;

namespace HSMServer.BackgroundServices.DatabaseServices
{
    // The pass of the large file store (#1883): once at start, after the tree is built (the cache builds it when it is
    // constructed, before any hosted service runs), with the check of the File sensors' rows; then every hour. Below normal
    // priority, like the merge of table blocks. It opens the store's records and creates nothing: a server without large
    // files only reads that the folder is not there.
    public sealed class LargeFileMaintenanceService : BaseDelayedBackgroundService
    {
        private readonly LargeFileUploadService _uploads;


        public override TimeSpan Delay { get; } = TimeSpan.FromHours(1);


        public LargeFileMaintenanceService(LargeFileUploadService uploads)
        {
            _uploads = uploads;
        }


        protected override async Task ExecuteAsync(CancellationToken token)
        {
            await Task.Run(() => RunPass(atStart: true, token), token);

            await base.ExecuteAsync(token);
        }

        protected override Task ServiceActionAsync(CancellationToken token = default)
        {
            RunPass(atStart: false, token);

            return Task.CompletedTask;
        }

        private void RunPass(bool atStart, CancellationToken token)
        {
            var thread = Thread.CurrentThread;
            var priority = thread.Priority;

            try
            {
                thread.Priority = ThreadPriority.BelowNormal;

                _uploads.RunPassAsync(DateTime.UtcNow, atStart, token).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception ex) when (token.IsCancellationRequested)
            {
                // Stopped by the server's stop: the cache and the history may be closing under the pass (an
                // ObjectDisposedException, #1958). Not a fault; the next start runs the pass again.
                _logger.Info($"The pass of the large file store stopped with the server{(atStart ? " at start" : string.Empty)}: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"The pass of the large file store failed{(atStart ? " at start" : string.Empty)}");
            }
            finally
            {
                thread.Priority = priority;
            }
        }
    }
}
