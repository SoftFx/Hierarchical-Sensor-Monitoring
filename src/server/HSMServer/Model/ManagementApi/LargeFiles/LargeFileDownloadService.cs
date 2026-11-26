using System;
using System.IO;
using System.Security.Claims;
using HSMServer.Authentication;
using HSMServer.BackgroundServices;
using HSMServer.Core.Cache;
using HSMServer.Core.LargeFiles.Archives;
using HSMServer.Core.LargeFiles.Downloads;
using HSMServer.Core.LargeFiles.DumpPasswords;
using HSMServer.Model.Authentication;
using Microsoft.Extensions.Logging;

namespace HSMServer.Model.ManagementApi.LargeFiles
{
    /// <summary>Who asks for a large file: a signed-in web user, or an API token acting as its owner.</summary>
    public sealed class LargeFileCaller
    {
        private LargeFileCaller(DownloadChannel channel, User webUser, ClaimsPrincipal principal, string clientIp)
        {
            Channel = channel;
            WebUser = webUser;
            Principal = principal;
            ClientIp = clientIp;
        }


        public DownloadChannel Channel { get; }

        /// <summary>The signed-in user of a web request; null for a token.</summary>
        public User WebUser { get; }

        /// <summary>The token principal of a management API request; null for a web user.</summary>
        public ClaimsPrincipal Principal { get; }

        public string ClientIp { get; }


        public static LargeFileCaller Web(User user, string clientIp) =>
            new(DownloadChannel.Web, user ?? throw new ArgumentNullException(nameof(user)), null, clientIp);

        public static LargeFileCaller Token(ClaimsPrincipal principal, string clientIp) =>
            new(DownloadChannel.Api, null, principal ?? throw new ArgumentNullException(nameof(principal)), clientIp);
    }


    public enum LargeFileDownloadOutcome
    {
        Ok,

        /// <summary>Unknown file, a file the caller may not see, a file in upload, a removed sensor or a replaced archive: one answer for all.</summary>
        NotFound,

        /// <summary>The caller sees the file, but it has no archive yet (no password, being packed, no room, the build fails).</summary>
        NotReady,

        /// <summary>The download could not be recorded, so it is refused.</summary>
        LogUnavailable,
    }


    /// <summary>What a download answer needs once it was allowed (and recorded). Never the password.</summary>
    public sealed record LargeFileArchiveDownload
    {
        public Guid FileId { get; init; }

        public Guid ArchiveId { get; init; }

        public Guid ProductId { get; init; }

        public long Length { get; init; }

        public byte[] Sha256 { get; init; }

        /// <summary>The kind of the file: false for an archive without encryption.</summary>
        public bool IsSecure { get; init; }

        /// <summary>The dump password version a secure archive opens with; null for a file that is not secure.</summary>
        public int? PasswordVersion { get; init; }

        public DateTime? PasswordSetUtc { get; init; }

        /// <summary>The name the browser saves the archive as: the sanitized original name plus ".zip".</summary>
        public string FileName { get; init; }

        /// <summary>"&lt;productId:N&gt;/&lt;archiveId:N&gt;.zip" under the archives folder: what the bundled Caddy serves.</summary>
        public string RelativePath { get; init; }

        public DateTime? LastModifiedUtc { get; init; }
    }


    public sealed record LargeFileDownloadDecision(LargeFileDownloadOutcome Outcome, string Reason = null, LargeFileArchiveDownload Download = null)
    {
        public static readonly LargeFileDownloadDecision NotFound = new(LargeFileDownloadOutcome.NotFound);
    }


    /// <summary>
    /// The decisions of the large file download (ADR-0022 points 13-15, #1885) for the web page, the management API and
    /// the MCP tool: whether a caller may download an archive, what to answer when not, the download record, and the
    /// description of a file. Transport-agnostic: the controllers render its decisions, nothing here writes a response.
    /// </summary>
    // The order is binding (design #1885, section 12.6): the store's record -> the sensor still in its product -> the
    // caller sees the product (else 404) -> the state (else 409) -> the archive id (else 404) -> the record (else 503).
    // Everything that may not disclose the file's existence answers the same NotFound, never a 403. The order and the
    // NotFound are the same for a secure file and for one that is not secure; only a secure file waits for a password.
    public sealed class LargeFileDownloadService
    {
        public const string WaitingForPasswordReason =
            "The file is kept but cannot be downloaded: its product has no dump password yet. A Manager of the product sets it in the product settings.";

        public const string BeingPackedReason = "The archive of the file is being packed. Try again later.";

        public const string WaitingForRoomReason =
            "The archive of the file waits for free disk space on the server; the server tries again by itself. Try again later.";

        public const string FailedReason =
            "Packing the archive of the file failed; the server tries again by itself and the file is kept. Try again later.";

        internal const string LogUnavailableMessage = "The download cannot be recorded right now, so it is refused. Try again later.";

        private readonly ILargeFileArchiveStore _store;
        private readonly ITreeValuesCache _cache;
        private readonly IApiTokenAuthorizationService _authorization;
        private readonly IApiTokenManager _tokens;
        private readonly IUserManager _users;
        private readonly IArchiveBuilder _builder;
        private readonly IDumpPasswordService _passwords;
        private readonly IDownloadLog _log;
        private readonly ILargeFilesMonitor _monitor;
        private readonly TimeProvider _time;
        private readonly ILogger<LargeFileDownloadService> _logger;


        public LargeFileDownloadService(ILargeFileArchiveStore store, ITreeValuesCache cache, IApiTokenAuthorizationService authorization,
            IApiTokenManager tokens, IUserManager users, IArchiveBuilder builder, IDumpPasswordService passwords, IDownloadLog log,
            ILargeFilesMonitor monitor, TimeProvider time, ILogger<LargeFileDownloadService> logger)
        {
            _store = store;
            _cache = cache;
            _authorization = authorization;
            _tokens = tokens;
            _users = users;
            _builder = builder;
            _passwords = passwords;
            _log = log;
            _monitor = monitor;
            _time = time;
            _logger = logger;
        }


        /// <summary>The management API path of an archive: the link names both the file and the archive.</summary>
        public static string ApiDownloadPath(Guid fileId, Guid archiveId) => $"/api/v1/large-files/{fileId}/archives/{archiveId}";

        /// <summary>The web path of an archive (cookie-authenticated).</summary>
        public static string WebDownloadPath(Guid fileId, Guid archiveId) => $"/LargeFiles/Download/{fileId}/{archiveId}";


        /// <summary>
        /// Decides a download of the archive <paramref name="archiveId"/> of the file <paramref name="fileId"/>. With
        /// <paramref name="record"/> (a GET, not a HEAD) an allowed download is written to the download log first, and a
        /// download that cannot be recorded is refused.
        /// </summary>
        public LargeFileDownloadDecision Decide(Guid fileId, Guid archiveId, LargeFileCaller caller, bool record)
        {
            ArgumentNullException.ThrowIfNull(caller);

            if (!TryGetVisibleFile(fileId, caller, out var file))
                return LargeFileDownloadDecision.NotFound;

            switch (file.State)
            {
                case LargeArchiveFileState.Packed:
                    break;

                case LargeArchiveFileState.Verified:
                    return new LargeFileDownloadDecision(LargeFileDownloadOutcome.NotReady, NotReadyReason(file));

                default:
                    // Uploading: the file is not a value yet; nothing about it is disclosed.
                    return LargeFileDownloadDecision.NotFound;
            }

            // A packed record always names its archive (and, when secure, its password version); one that does not is a
            // store defect and is not served.
            if (file.ArchiveId != archiveId || !IsPackedRecordWhole(file) || file.ArchiveLength is not { } archiveLength ||
                ArchiveRelativePath(file, archiveId) is not { } relativePath)
                return LargeFileDownloadDecision.NotFound;

            var passwordVersion = CarriedVersion(file);

            var download = new LargeFileArchiveDownload
            {
                FileId = file.FileId,
                ArchiveId = archiveId,
                ProductId = file.ProductId,
                Length = archiveLength,
                Sha256 = file.ArchiveSha256,
                IsSecure = file.IsSecure,
                PasswordVersion = passwordVersion,
                PasswordSetUtc = passwordVersion is int version ? _passwords.GetVersion(file.ProductId, version)?.SetUtc : null,
                FileName = LargeFileNames.DownloadFileName(file.Name, file.Extension),
                RelativePath = relativePath,
                LastModifiedUtc = TryGetArchiveTime(file),
            };

            if (record && !TryRecord(caller, file, download))
                return new LargeFileDownloadDecision(LargeFileDownloadOutcome.LogUnavailable, LogUnavailableMessage);

            return new LargeFileDownloadDecision(LargeFileDownloadOutcome.Ok, Download: download);
        }


        /// <summary>
        /// Opens the archive an allowed download names, or null when it is no longer the file's current archive (a new
        /// password replaced it after the decision, or the file was removed). The record is read again AFTER the open: an
        /// archive replaced before the open is refused, one replaced after it is still the bytes the link named (the store
        /// opens with FileShare.Delete), so a resumed download never mixes two archives.
        /// </summary>
        public Stream TryOpenArchive(LargeFileArchiveDownload download)
        {
            Stream stream;

            try
            {
                stream = _store.OpenArchive(download.FileId);
            }
            catch (IOException ex)
            {
                _logger.LogInformation("The archive {ArchiveId} of the large file {FileId} could not be opened: {Error}", download.ArchiveId, download.FileId, ex.Message);
                return null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Access denied or a fault of the store: the surface answers its NotFound, never a 500. The open involves no
                // password, so the text is logged as it is.
                _logger.LogWarning("The archive {ArchiveId} of the large file {FileId} could not be opened: {ErrorType}: {Error}",
                    download.ArchiveId, download.FileId, ex.GetType().Name, ex.Message);
                return null;
            }

            if (_store.TryGetFile(download.FileId) is { State: LargeArchiveFileState.Packed } file && file.ArchiveId == download.ArchiveId)
                return stream;

            stream.Dispose();
            return null;
        }


        /// <summary>
        /// The description of a large file value of a sensor for a token: the REST twin of the MCP tool. Unknown files,
        /// files of another sensor, files in upload and files the token's owner may not see answer the same NotFound.
        /// </summary>
        public ManagementReadResult<LargeFileDto> Describe(Guid sensorId, Guid fileId, ClaimsPrincipal principal, string baseUrl)
        {
            ArgumentNullException.ThrowIfNull(principal);

            if (!TryGetVisibleFile(fileId, LargeFileCaller.Token(principal, null), out var file) || file.SensorId != sensorId ||
                ToDto(file, baseUrl) is not { } dto)
                return ManagementReadResult<LargeFileDto>.Fail(ManagementReadOutcome.NotFound);

            return ManagementReadResult<LargeFileDto>.Ok(dto);
        }

        /// <summary>
        /// The description of a large file without any caller check, for a surface that has already decided who may see
        /// it (the sensor page). Null where the described file is not a value: the store does not know it, it is still
        /// being uploaded, or its sensor is gone or now belongs to another product than the one it was uploaded to.
        /// The caller also checks that <c>SensorId</c> of the answer is the sensor it shows, and links a signed-in user to
        /// <see cref="WebDownloadPath"/>: <c>DownloadUrl</c> is the API route, which takes a token, not a cookie.
        /// </summary>
        public LargeFileDto DescribeFile(Guid fileId, string baseUrl)
        {
            var file = _store.TryGetFile(fileId);

            return file is not null && IsInItsProduct(file) ? ToDto(file, baseUrl) : null;
        }


        // Null for a file that is not a value yet (uploading).
        private LargeFileDto ToDto(LargeArchiveFile file, string baseUrl)
        {
            if (file.State is not (LargeArchiveFileState.Verified or LargeArchiveFileState.Packed))
                return null;

            var status = _builder.GetStatus(file.FileId);
            var packed = file.State == LargeArchiveFileState.Packed && IsPackedRecordWhole(file);
            var passwordVersion = CarriedVersion(file);

            return new LargeFileDto
            {
                FileId = file.FileId,
                SensorId = file.SensorId,
                Name = file.Name,
                Extension = file.Extension,
                Size = file.Size,
                SourceModifiedUtc = file.SourceModifiedUtc,
                Secure = file.IsSecure,
                State = packed ? "packed" : "verified",
                BuildState = ToWire(status?.State ?? ArchiveBuildState.None),
                NotReadyReason = packed ? null : NotReadyReason(file),
                Archive = packed
                    ? new LargeFileArchiveDto
                    {
                        Id = file.ArchiveId.Value,
                        Size = file.ArchiveLength.Value,
                        Sha256 = Convert.ToHexString(file.ArchiveSha256).ToLowerInvariant(),
                        PasswordVersion = passwordVersion,
                        PasswordSetUtc = passwordVersion is int version ? _passwords.GetVersion(file.ProductId, version)?.SetUtc : null,
                        DownloadUrl = $"{baseUrl?.TrimEnd('/')}{ApiDownloadPath(file.FileId, file.ArchiveId.Value)}",
                    }
                    : null,
            };
        }

        // Steps 1-3 of the order: the store's record, the sensor still in the product the file was uploaded to, the caller's
        // sight of that product. A sensor moved to another product, or removed, takes its files out of reach.
        private bool TryGetVisibleFile(Guid fileId, LargeFileCaller caller, out LargeArchiveFile file)
        {
            file = _store.TryGetFile(fileId);

            if (file is null || !IsInItsProduct(file))
                return false;

            return caller.WebUser is { } user
                ? user.IsProductAvailable(file.ProductId)
                : _authorization.AuthorizeRead(caller.Principal, ApiTokenResource.Sensor(file.SensorId)) == ApiTokenAuthorization.Allowed;
        }

        // The file's sensor exists and its root product is still the product the file was uploaded to.
        private bool IsInItsProduct(LargeArchiveFile file) => _cache.GetSensor(file.SensorId)?.Parent?.Root.Id is { } rootId && rootId == file.ProductId;

        // A packed record names its archive, its length and hash, and, for a secure file, the version it opens with.
        private static bool IsPackedRecordWhole(LargeArchiveFile file) =>
            file.ArchiveId is not null && file.ArchiveLength is not null && file.ArchiveSha256 is not null &&
            (!file.IsSecure || CarriedVersion(file) is not null);

        // The dump password version a secure archive opens with; null (and 0) is "no version", always so when not secure.
        private static int? CarriedVersion(LargeArchiveFile file) =>
            file.IsSecure && file.PasswordVersion is int version && version > 0 ? version : null;

        // Only a secure file waits for a password; one that is not secure is packed whatever the password.
        private string NotReadyReason(LargeArchiveFile file)
        {
            if (file.IsSecure && !_passwords.HasPassword(file.ProductId))
                return WaitingForPasswordReason;

            return _builder.GetStatus(file.FileId)?.State switch
            {
                ArchiveBuildState.WaitingForPassword when file.IsSecure => WaitingForPasswordReason,
                ArchiveBuildState.WaitingForRoom => WaitingForRoomReason,
                ArchiveBuildState.Failed => FailedReason,
                _ => BeingPackedReason,
            };
        }

        // The download log records both kinds: a file that is not secure with password version 0.
        private bool TryRecord(LargeFileCaller caller, LargeArchiveFile file, LargeFileArchiveDownload download)
        {
            var target = new DownloadTarget(file.ProductId, file.SensorId, file.FileId, download.ArchiveId, download.PasswordVersion ?? 0, download.FileName);

            try
            {
                _log.Record(ToDownloadCaller(caller), target, _time.GetUtcNow().UtcDateTime);
                _monitor.ReportDownloadLog(true, null);

                return true;
            }
            catch (DownloadLogException ex)
            {
                _logger.LogError(ex, "A download of the large file {FileId} was refused: the download log could not be written", file.FileId);
                _monitor.ReportDownloadLog(false, ex.Message);

                return false;
            }
        }

        private DownloadCaller ToDownloadCaller(LargeFileCaller caller)
        {
            if (caller.WebUser is { } user)
                return new DownloadCaller(DownloadChannel.Web, user.Id, user.Name, null, null, caller.ClientIp);

            // The principal passed AuthorizeRead a moment ago, so its claims resolve; a token revoked in between still
            // gets its download recorded under the ids it carried.
            var ownerClaim = caller.Principal.FindFirst(HsmApiTokenClaims.OwnerUserId)?.Value;
            var tokenClaim = caller.Principal.FindFirst(HsmApiTokenClaims.TokenId)?.Value;

            var ownerId = Guid.TryParse(ownerClaim, out var parsed) ? parsed : Guid.Empty;
            var token = tokenClaim is null ? null : _tokens.GetToken(tokenClaim);

            return new DownloadCaller(DownloadChannel.Api, ownerId, _users[ownerId]?.Name, token?.EntityId, token?.Name, caller.ClientIp);
        }

        // The path the bundled Caddy serves, relative to the archives folder it mounts. Taken from the store's own path of
        // the archive, so the hand-over and the store cannot drift apart, and accepted only when it is the form made of the
        // two GUIDs of the record (LargeArchivePaths): nothing a client sent ever reaches the path. Null otherwise, logged.
        private string ArchiveRelativePath(LargeArchiveFile file, Guid archiveId)
        {
            var expected = LargeArchivePaths.RelativeArchivePath(file.ProductId, archiveId);
            string relative;

            try
            {
                relative = Path.GetRelativePath(_store.ArchivesFolder, _store.ArchivePath(file)).Replace(Path.DirectorySeparatorChar, '/');
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning("The path of the archive {ArchiveId} of the large file {FileId} could not be read: {ErrorType}: {Error}",
                    archiveId, file.FileId, ex.GetType().Name, ex.Message);
                return null;
            }

            if (string.Equals(relative, expected, StringComparison.Ordinal))
                return relative;

            _logger.LogError("The store places the archive {ArchiveId} of the large file {FileId} at {Path}, not at {Expected} under its archives folder",
                archiveId, file.FileId, relative, expected);
            return null;
        }

        private DateTime? TryGetArchiveTime(LargeArchiveFile file)
        {
            try
            {
                var path = _store.ArchivePath(file);

                return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                return null;
            }
        }

        private static string ToWire(ArchiveBuildState state) => state switch
        {
            ArchiveBuildState.WaitingForPassword => "waitingForPassword",
            ArchiveBuildState.Queued => "queued",
            ArchiveBuildState.Building => "building",
            ArchiveBuildState.WaitingForRoom => "waitingForRoom",
            ArchiveBuildState.Failed => "failed",
            _ => "none",
        };
    }
}
