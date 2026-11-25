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


    /// <summary>One file value in the list of a File sensor's page.</summary>
    public sealed class FileValueRowViewModel
    {
        /// <summary>The time of the value: the key of its row, and what a download or a preview asks for.</summary>
        public long TimeTicks { get; init; }

        /// <summary>An element id made of the time only (digits), never of anything a client sent.</summary>
        public string RowId => $"file_row_{TimeTicks}";

        public DateTime Time { get; init; }

        public string Name { get; init; }

        public string Extension { get; init; }

        public string Size { get; init; }

        public SensorStatus Status { get; init; }

        public string Comment { get; init; }

        /// <summary>A text preview of an ordinary csv or txt file small enough; never for a large value.</summary>
        public bool IsPreviewEnabled { get; init; }


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
        };
    }


    /// <summary>A page of the file values of one File sensor, newest first.</summary>
    public sealed class FileValuesPageViewModel
    {
        public string EncodedId { get; init; }

        public IReadOnlyList<FileValueRowViewModel> Rows { get; init; } = [];

        /// <summary>The time to ask the next (older) page before; null when there is none.</summary>
        public long? OlderThanTicks { get; init; }
    }
}
