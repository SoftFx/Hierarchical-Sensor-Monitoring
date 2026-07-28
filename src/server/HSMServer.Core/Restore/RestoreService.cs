using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HSMDatabase.AccessManager;
using HSMDatabase.LevelDB;
using HSMDatabase.LevelDB.DatabaseImplementations;
using HSMServer.Core.Cache;
using HSMServer.Core.Model;
using NLog;

namespace HSMServer.Core.Restore
{
    public sealed class RestoreService : IRestoreService
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        private static readonly TimeSpan SessionIdleTimeout = TimeSpan.FromMinutes(10);

        private readonly IDatabaseSettings _dbSettings;
        private readonly ITreeValuesCache _cache;
        private readonly Timer _expiryTimer;

        private readonly ConcurrentDictionary<Guid, BackupSession> _sessions = new();

        // Default ctor used by DI when IDatabaseSettings isn't registered. Matches the
        // BackupDatabaseService convention which also news up DatabaseSettings directly.
        public RestoreService(IDatabaseSettings dbSettings, ITreeValuesCache cache)
        {
            _dbSettings = dbSettings;
            _cache = cache;

            _expiryTimer = new Timer(_ => SweepExpired(), null, SessionIdleTimeout, SessionIdleTimeout);
        }

        public List<BackupFileInfo> ListBackups()
        {
            var folder = _dbSettings.DatabaseBackupsFolder;
            var prefix = $"{_dbSettings.EnvironmentDatabaseName}_";

            var result = new List<BackupFileInfo>();

            try
            {
                if (!Directory.Exists(folder))
                    return result;

                foreach (var file in new DirectoryInfo(folder).EnumerateFiles($"{prefix}*.zip"))
                {
                    result.Add(new BackupFileInfo
                    {
                        FileName = file.Name,
                        LastWriteTimeUtc = file.LastWriteTimeUtc,
                        SizeBytes = file.Length,
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to enumerate backups in {folder}");
            }

            return result;
        }

        public Guid OpenBackup(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                throw new ArgumentException("Backup file name is required.", nameof(fileName));

            var backupsFolder = Path.GetFullPath(_dbSettings.DatabaseBackupsFolder);
            var envPrefix = $"{_dbSettings.EnvironmentDatabaseName}_";

            // Path-traversal guard: only allow a bare file name that resolves under backupsFolder.
            var resolved = Path.GetFullPath(Path.Combine(backupsFolder, Path.GetFileName(fileName)));
            var expectedPrefix = Path.Combine(backupsFolder, envPrefix);
            if (!resolved.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"File '{fileName}' is not a valid environment backup.");

            if (!File.Exists(resolved))
                throw new FileNotFoundException($"Backup file '{fileName}' not found.", resolved);

            var sessionId = Guid.NewGuid();
            var tempRoot = _dbSettings.DatabaseRestoreTempFolder;
            Directory.CreateDirectory(tempRoot);

            var tempPath = Path.Combine(tempRoot, $"restore_{sessionId:N}");
            Directory.CreateDirectory(tempPath);

            try
            {
                ZipFile.ExtractToDirectory(resolved, tempPath);
            }
            catch (Exception ex)
            {
                TryDeleteDirectory(tempPath);
                throw new InvalidOperationException($"Failed to unpack backup '{fileName}': {ex.Message}", ex);
            }

            EnvironmentDatabaseWorker worker;
            try
            {
                var adapter = LevelDBDatabaseAdapter.ForReadOnly(tempPath);
                worker = new EnvironmentDatabaseWorker(adapter);
            }
            catch (Exception ex)
            {
                TryDeleteDirectory(tempPath);
                throw new InvalidOperationException($"Failed to open unpacked backup '{fileName}' as a LevelDB: {ex.Message}", ex);
            }

            var session = new BackupSession(sessionId, worker, tempPath, fileName);

            if (!_sessions.TryAdd(sessionId, session))
            {
                session.Dispose();
                throw new InvalidOperationException("Session id collision; retry.");
            }

            _logger.Info($"Opened backup '{fileName}' as session {sessionId} at {tempPath}");
            return sessionId;
        }

        public List<BackupTemplateItem> ListAlertTemplates(Guid session)
        {
            var backup = GetSessionOrThrow(session);

            // One-time snapshot of live Ids so ExistsOnLive reflects a consistent view across
            // all rows even if a concurrent add/remove happens mid-iteration.
            var liveIds = _cache.GetAlertTemplateModels()?
                                .Select(t => t.Id)
                                .ToHashSet() ?? new HashSet<Guid>();

            var result = new List<BackupTemplateItem>();

            foreach (var idBytes in backup.Worker.GetAllAlertTemplatesIds())
            {
                try
                {
                    var entity = backup.Worker.GetAlertTemplate(idBytes);
                    if (entity == null)
                        continue;

                    var id = new Guid(entity.Id);
                    result.Add(new BackupTemplateItem
                    {
                        Id = id,
                        Name = entity.Name,
                        ExistsOnLive = liveIds.Contains(id),
                    });
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"Failed to read alert template from backup session {session}");
                }
            }

            return result;
        }

        public async Task<RestoreResult> RestoreTemplatesAsync(Guid session, List<RestoreRequestItem> items, string adminUserName, CancellationToken cancellationToken = default)
        {
            if (items == null)
                throw new ArgumentNullException(nameof(items));

            var backup = GetSessionOrThrow(session);

            // Pin the session for the whole batch so the 10-min idle expiry timer can't rip it
            // out from under items 2..N. RestoreOneAsync still re-checks the session per item
            // via _sessions, but the in-flight guard is the durable protection.
            backup.BeginInFlightRestore();
            try
            {
                _logger.Info($"{adminUserName} started restore from '{backup.SourceBackupFileName}': " +
                             string.Join(", ", items.Select(i => $"{i.Id}:{i.Resolution}")));

                var result = new RestoreResult();

                foreach (var item in items)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var entity = backup.Worker.GetAlertTemplate(item.Id.ToByteArray());
                    if (entity == null)
                    {
                        result.Items.Add(new RestoreResultItem
                        {
                            Id = item.Id,
                            Name = "(missing)",
                            Resolution = item.Resolution,
                            Outcome = "error: template not found in backup",
                        });
                        continue;
                    }

                    var displayName = entity.Name ?? item.Id.ToString();

                    switch (item.Resolution)
                    {
                        case CollisionResolution.Skip:
                            result.Items.Add(new RestoreResultItem
                            {
                                Id = item.Id,
                                Name = displayName,
                                Resolution = item.Resolution,
                                Outcome = "skipped",
                            });
                            break;

                        case CollisionResolution.Overwrite:
                            result.Items.Add(await RestoreOneAsync(item, entity, displayName, keepId: true, adminUserName, cancellationToken));
                            break;

                        case CollisionResolution.Duplicate:
                            result.Items.Add(await RestoreOneAsync(item, entity, displayName, keepId: false, adminUserName, cancellationToken));
                            break;

                        default:
                            result.Items.Add(new RestoreResultItem
                            {
                                Id = item.Id,
                                Name = displayName,
                                Resolution = item.Resolution,
                                Outcome = $"error: unknown resolution {item.Resolution}",
                            });
                            break;
                    }
                }

                _logger.Info($"{adminUserName} restore finished: " +
                             string.Join(", ", result.Items.Select(r => $"{r.Name}={r.Outcome}")));

                return result;
            }
            finally
            {
                backup.EndInFlightRestore();
            }
        }

        public void CloseSession(Guid session)
        {
            if (_sessions.TryRemove(session, out var backup))
            {
                backup.Dispose();
                _logger.Info($"Closed backup session {session}");
            }
            else
            {
                // Likely a double-close, or the session already expired (10-min idle). Not an
                // error, but worth logging so the operator can correlate "I closed the wizard
                // but the temp folder lingered" cases.
                _logger.Warn($"CloseSession called for unknown session {session} (already expired or already closed)");
            }
        }

        private async Task<RestoreResultItem> RestoreOneAsync(RestoreRequestItem item,
                                                              HSMDatabase.AccessManager.DatabaseEntities.AlertTemplateEntity entity,
                                                              string displayName,
                                                              bool keepId,
                                                              string adminUserName,
                                                              CancellationToken cancellationToken)
        {
            var model = new AlertTemplateModel(entity);

            if (!keepId)
            {
                model.Id = Guid.NewGuid();
                model.Name = $"{entity.Name} (restored {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss})";
            }

            // AddAlertTemplateAsync does NOT enforce unique Name (the controller does, on its
            // own save path). Without this check, restoring a template whose Name collides with
            // a different live template would silently create a duplicate Name in the cache.
            // For Duplicate we already substituted a new Name with a timestamp suffix, but a
            // second restore within the same second would collide — check both branches.
            var existingWithSameName = _cache.GetAlertTemplateModels()?
                                             .FirstOrDefault(t => t.Name == model.Name && t.Id != model.Id);
            if (existingWithSameName != null)
            {
                return new RestoreResultItem
                {
                    Id = item.Id,
                    Name = displayName,
                    Resolution = item.Resolution,
                    Outcome = $"error: a template with name '{model.Name}' already exists (Id {existingWithSameName.Id})",
                };
            }

            string outcome;
            try
            {
                var existsBefore = _cache.GetAlertTemplate(item.Id) != null;
                var (success, error) = await _cache.AddAlertTemplateAsync(model, cancellationToken);

                if (!success)
                {
                    outcome = $"error: {error}";
                }
                else if (!keepId)
                {
                    outcome = $"duplicated as {model.Id}";
                }
                else if (existsBefore)
                {
                    outcome = "overwritten";
                }
                else
                {
                    outcome = "inserted";
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"{adminUserName}: failed to restore template {item.Id} ({displayName})");
                outcome = $"error: {ex.Message}";
            }

            return new RestoreResultItem
            {
                Id = item.Id,
                Name = displayName,
                Resolution = item.Resolution,
                Outcome = outcome,
            };
        }

        private BackupSession GetSessionOrThrow(Guid session)
        {
            if (!_sessions.TryGetValue(session, out var backup))
                throw new InvalidOperationException($"Restore session {session} not found. It may have expired (idle timeout {SessionIdleTimeout.TotalMinutes:F0} min) — reopen the backup and try again.");

            backup.Touch();
            return backup;
        }

        private void SweepExpired()
        {
            var cutoff = DateTime.UtcNow - SessionIdleTimeout;
            foreach (var (id, session) in _sessions)
            {
                // Skip sessions that are mid-restore: a long RestoreTemplatesAsync batch can
                // outlive the idle window, but the session must stay alive until the batch
                // finishes. RestoreOneAsync keeps LastAccessUtc fresh per item, but a slow
                // AddAlertTemplateAsync (folder with many sensors) could still span the window
                // for a single item — HasInFlightRestore is the authoritative guard.
                if (session.HasInFlightRestore)
                    continue;

                if (session.LastAccessUtc < cutoff && _sessions.TryRemove(id, out var expired))
                {
                    try { expired.Dispose(); }
                    catch (Exception ex) { _logger.Error(ex, $"Failed to dispose expired session {id}"); }

                    _logger.Info($"Expired idle restore session {id}");
                }
            }
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
            }
            catch
            {
                // Swallow: startup sweep (RestoreTempCleanupService) handles leftovers.
            }
        }
    }
}
