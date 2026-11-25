using System;
using System.Collections.Generic;
using HSMCommon.Model;
using HSMServer.Core.Extensions;

namespace HSMServer.Model.FileSensors
{
    /// <summary>
    /// What the sensor page reads about the last value of a File sensor (#1886): its description, never its bytes. The
    /// same answer for an ordinary file value and a large one.
    /// </summary>
    public sealed record FileInfoDto
    {
        public DateTime Time { get; init; }

        public DateTime ReceivingTime { get; init; }

        /// <summary>The name the value is shown under (<see cref="FileValueNameExtensions.GetDisplayName"/>).</summary>
        public string Name { get; init; }

        public string Extension { get; init; }

        public long OriginalSize { get; init; }

        /// <summary>True for a large file value: no preview, its own download.</summary>
        public bool IsLarge { get; init; }


        public static FileInfoDto From(FileValue file) => file is null ? null : new FileInfoDto
        {
            Time = file.Time,
            ReceivingTime = file.ReceivingTime,
            Name = file.GetDisplayName(),
            Extension = file.Extension,
            OriginalSize = file.OriginalSize,
            IsLarge = FileSensorPageService.IsLarge(file),
        };
    }


    /// <summary>
    /// The state of a large file value on the sensor page, in the order of its life. A large value leaves the list with its
    /// history (retention, a clear); "missing" is a value whose file the store has no record of.
    /// </summary>
    public enum LargeFileRowState
    {
        Uploading,
        Checking,
        WaitingForPassword,
        Packing,
        WaitingForRoom,
        PackingFailed,
        Ready,
        Missing,
    }


    /// <summary>One file value in the list of a File sensor's page.</summary>
    public sealed class FileValueRowViewModel
    {
        /// <summary>What a row shows where the store has no application or host.</summary>
        public const string Unknown = "—";

        /// <summary>The time of the value: the key of its row, and what a download or a preview asks for.</summary>
        public long TimeTicks { get; init; }

        /// <summary>An element id made of the time only (digits), never of anything a client sent.</summary>
        public string RowId => $"file_row_{TimeTicks}";

        /// <summary>The time the value was accepted (for a large value: the time the server verified the file).</summary>
        public DateTime Time { get; init; }

        public string Name { get; init; }

        public string Extension { get; init; }

        public string Size { get; init; }

        public SensorStatus Status { get; init; }

        public string Comment { get; init; }

        /// <summary>A text preview of an ordinary csv or txt file small enough; never for a large value.</summary>
        public bool IsPreviewEnabled { get; init; }


        public bool IsLarge { get; init; }

        /// <summary>The kind of a large file: "secure" (its archive under the dump password) or "not secure".</summary>
        public string Kind { get; init; }

        public LargeFileRowState LargeState { get; init; }

        public string StateText { get; init; }

        /// <summary>Why a large file cannot be downloaded yet; null when it can.</summary>
        public string Reason { get; init; }

        /// <summary>The download of a ready large file (its archive); null otherwise.</summary>
        public string DownloadUrl { get; init; }

        /// <summary>The modification time of the source file of a large value.</summary>
        public DateTime? SourceModified { get; init; }

        public string Application { get; init; } = Unknown;

        public string Host { get; init; } = Unknown;

        /// <summary>Who downloaded the file and when; null for a user who is not a Manager of the product or an admin.</summary>
        public IReadOnlyList<LargeFileDownloadFacts> Downloads { get; init; }

        public bool CanDownload => IsLarge ? DownloadUrl is not null : true;


        internal static FileValueRowViewModel From(FileValue file) => new()
        {
            TimeTicks = file.Time.Ticks,
            Time = file.Time,
            Name = file.GetDisplayName(),
            Extension = file.Extension,
            Size = file.FileSizeToNormalString(),
            Status = file.Status,
            Comment = file.Comment,
            IsPreviewEnabled = FileSensorPageService.IsPreviewEnabled(file),
            IsLarge = FileSensorPageService.IsLarge(file),
        };
    }


    /// <summary>An upload of the sensor that is not a value yet.</summary>
    public sealed class LargeFileUploadRowViewModel
    {
        public Guid FileId { get; init; }

        public string Name { get; init; }

        public string Size { get; init; }

        public string Kind { get; init; }

        public LargeFileRowState State { get; init; }

        public string StateText { get; init; }

        public int Percent { get; init; }

        public DateTime StartedUtc { get; init; }

        public DateTime SourceModified { get; init; }

        public string Application { get; init; }

        public string Host { get; init; }
    }


    /// <summary>A page of the file values of one File sensor, newest first.</summary>
    public sealed class FileValuesPageViewModel
    {
        public string EncodedId { get; init; }

        /// <summary>The uploads in progress, on the first page only, at most a page of them.</summary>
        public IReadOnlyList<LargeFileUploadRowViewModel> Uploads { get; init; } = [];

        /// <summary>Uploads in progress beyond those listed.</summary>
        public int MoreUploads { get; init; }

        public IReadOnlyList<FileValueRowViewModel> Rows { get; init; } = [];

        /// <summary>The time to ask the next (older) page before; null when there is none.</summary>
        public long? OlderThanTicks { get; init; }

        /// <summary>The product has no dump password and the page lists secure large files: say what is missing and who sets it.</summary>
        public bool ShowPasswordMissing { get; init; }

        /// <summary>The user may set the password (a Manager of the product or an admin): the notice links the settings.</summary>
        public string PasswordSettingsUrl { get; init; }
    }
}
