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
        /// The name a file value is shown under: the sensor page, the alert variable <c>$fileName</c>. Every file value, an
        /// ordinary one and a large one alike, carries its name without the extension and the extension apart (a large value
        /// is split so by <see cref="StemOf"/>), so the name is the two joined: "dump" + "dmp", "backup.tar" + "gz",
        /// "" + "env" (a dotfile), "dump" + nothing.
        /// </summary>
        public static string GetDisplayName(this FileValue file)
        {
            if (file is null)
                return string.Empty;

            var name = file.Name ?? string.Empty;
            var extension = (file.Extension ?? string.Empty).TrimStart('.');

            return extension.Length == 0 ? name : $"{name}.{extension}";
        }

        /// <summary>
        /// The name of a large value (#1886): the original file name without its extension, as every file value carries its
        /// name, so that the name and the extension join back into the original exactly. The extension is the one the
        /// server took from the same name (after its last dot); a name that does not end with it is kept whole.
        /// </summary>
        public static string StemOf(string fileName, string extension)
        {
            if (string.IsNullOrEmpty(fileName) || string.IsNullOrEmpty(extension))
                return fileName;

            var suffix = $".{extension}";

            return fileName.EndsWith(suffix, System.StringComparison.Ordinal) ? fileName[..^suffix.Length] : fileName;
        }
    }
}
