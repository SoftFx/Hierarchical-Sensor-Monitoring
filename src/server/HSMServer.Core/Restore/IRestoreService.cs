using System;
using System.Collections.Generic;
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

        Task<RestoreResult> RestoreTemplatesAsync(Guid session, List<RestoreRequestItem> items, string adminUserName);

        // Releases the session (closes the LevelDB handle and deletes the temp folder).
        // Optional: the service also expires idle sessions on a timer, but explicit close
        // is preferred so the temp DB is reclaimed as soon as the wizard ends.
        void CloseSession(Guid session);
    }
}
