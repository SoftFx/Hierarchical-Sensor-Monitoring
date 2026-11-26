using System.IO;
using HSMCommon.Model;

namespace HSMServer.Core.Extensions
{
    public static class FileValueNameExtensions
    {
        /// <summary>True for a large file value (ADR-0022): its content is in the large file store, not in the value.</summary>
        public static bool IsLargeFile(this FileValue file) => file is LargeFileValue;

        /// <summary>The id of a large value's file in the large file store; null for an ordinary file value.</summary>
        public static System.Guid? LargeFileIdOrDefault(this FileValue file) => (file as LargeFileValue)?.FileId;

        /// <summary>The modification time of a large value's source file; null for an ordinary file value.</summary>
        public static System.DateTime? LargeFileSourceModifiedOrDefault(this FileValue file) => (file as LargeFileValue)?.SourceModified;

        /// <summary>
        /// The name a file value is shown under (the sensor page, the alert variable <c>$fileName</c>, the management API).
        /// An ordinary file value usually carries its name without the extension and the extension apart; a large file value
        /// (#1886) carries the whole original name. A name that already ends with the value's extension is shown as it is,
        /// so nothing prints "dump.dmp.dmp"; any other name gets the extension ("backup.2026-11-25" + "csv").
        /// </summary>
        public static string GetDisplayName(this FileValue file)
        {
            if (file is null)
                return string.Empty;

            var name = file.Name ?? string.Empty;
            var extension = (file.Extension ?? string.Empty).TrimStart('.');

            if (name.Length == 0)
                return extension;

            if (extension.Length == 0 || name.EndsWith($".{extension}", System.StringComparison.OrdinalIgnoreCase))
                return name;

            return $"{name}.{extension}";
        }
    }
}
