using System;
using HSMCommon.Model;
using HSMServer.Core.LargeFiles.Archives;
using HSMServer.Core.Model.HistoryValues;

namespace HSMServer.Core.LargeFiles
{
    /// <summary>
    /// The state of a large file value as the read surfaces name it (#1885): the store's record, or what is known when
    /// there is none. A value row is written only once its file is verified, so a value never names an uploading file.
    /// </summary>
    public static class LargeFileValueStates
    {
        public const string Uploading = "uploading";
        public const string Verified = "verified";
        public const string Packed = "packed";

        /// <summary>The store no longer holds the file (its value outlived it, or the file was removed).</summary>
        public const string Removed = "removed";

        /// <summary>The store could not be read when the value was described.</summary>
        public const string Unavailable = "unavailable";


        /// <summary>The name of the record's state; <see cref="Removed"/> for no record.</summary>
        public static string Of(LargeArchiveFile record) => record?.State switch
        {
            null => Removed,
            LargeArchiveFileState.Uploading => Uploading,
            LargeArchiveFileState.Verified => Verified,
            LargeArchiveFileState.Packed => Packed,
            var other => other.ToString().ToLowerInvariant(),
        };
    }


    /// <summary>The description of a large file value for the history an access key reads: never its content.</summary>
    public static class LargeFileValueDescriptions
    {
        /// <summary>
        /// The value's own description with the kind and the state from the store's record. Without a store (a surface
        /// that does not read it) the kind and the state are null.
        /// </summary>
        public static LargeFileHistory Describe(LargeFileValue value, ILargeFileArchiveStore store)
        {
            ArgumentNullException.ThrowIfNull(value);

            LargeArchiveFile record = null;
            string state = null;

            if (store is not null)
            {
                try
                {
                    record = store.TryGetFile(value.FileId);
                    state = LargeFileValueStates.Of(record);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // The store reports and counts its own faults; the history is still read, the file described without it.
                    state = LargeFileValueStates.Unavailable;
                }
            }

            return new LargeFileHistory
            {
                FileId = value.FileId,
                Name = value.Name,
                Extension = value.Extension,
                Size = value.OriginalSize,
                Secure = record?.IsSecure,
                State = state,
            };
        }

        /// <summary>One line for a CSV cell: the file id, the size and, when known, the kind and the state.</summary>
        public static string ToText(LargeFileHistory description)
        {
            ArgumentNullException.ThrowIfNull(description);

            var text = $"Large file {description.FileId}; {description.Size} bytes";

            if (description.Secure is bool secure)
                text += secure ? "; secure" : "; not secure";

            if (description.State is { } state)
                text += $"; {state}";

            return text;
        }
    }
}
