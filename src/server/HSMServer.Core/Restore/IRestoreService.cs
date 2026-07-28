using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HSMServer.Core.Restore
{
    public interface IRestoreService
    {
        List<BackupFileInfo> ListBackups();

        // Opens (extracts + opens read-only) an EnvironmentData backup by file name.
        // Returns a session id the caller passes back to ListAlertTemplates / RestoreTemplates.
        // Throws if the file is missing, has the wrong prefix, or sits outside DatabaseBackupsFolder.
        Guid OpenBackup(string fileName);

        List<BackupTemplateItem> ListAlertTemplates(Guid session);

        // The cancellation token lets the controller abort a long restore if the client
        // disconnects. Restore also pins the session for the duration of the call so the
        // idle-expiry timer can't reap it mid-batch.
        Task<RestoreResult> RestoreTemplatesAsync(Guid session, List<RestoreRequestItem> items, string adminUserName, CancellationToken cancellationToken = default);

        // Releases the session (closes the LevelDB handle and deletes the temp folder).
        // Optional: the service also expires idle sessions on a timer, but explicit close
        // is preferred so the temp DB is reclaimed as soon as the wizard ends.
        void CloseSession(Guid session);
    }
}
