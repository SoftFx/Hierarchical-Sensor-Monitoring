using System;
using System.Globalization;
using System.Text;

namespace HSMServer.Core.LargeFiles.Archives
{
    /// <summary>
    /// The name of the only entry of an archive, made from the name a collector sent: a bare file name that every archiver
    /// extracts into the folder the user chose, never a path.
    /// </summary>
    public static class ArchiveEntryNames
    {
        public const string Fallback = "file";

        public const int MaxLength = 200;

        public const int MaxExtensionLength = 32;

        // Characters Windows does not allow in a file name; replaced so the entry extracts on every system.
        private const string InvalidChars = "<>:\"|?*";

        private static readonly string[] _reservedWindowsNames =
        [
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        ];


        /// <summary>
        /// Strips any directory part, drive letter, ".." and control or format characters; an empty result is
        /// <see cref="Fallback"/>; appends <paramref name="extension"/> when the name has none; at most <see cref="MaxLength"/> characters.
        /// </summary>
        public static string Sanitize(string name, string extension)
        {
            var clean = Clean(name);

            // Only the last path segment is kept: "../../etc/passwd" becomes "passwd", "C:\dumps\a.dmp" becomes "a.dmp".
            var lastSeparator = clean.LastIndexOfAny(['/', '\\']);
            if (lastSeparator >= 0)
                clean = clean[(lastSeparator + 1)..];

            // A drive-relative name such as "C:a.dmp".
            if (clean.Length >= 2 && clean[1] == ':' && IsAsciiLetter(clean[0]))
                clean = clean[2..];

            // Windows drops trailing dots and spaces, so "..", "..." and "name. ." are not names of their own.
            clean = ReplaceInvalid(clean).TrimStart(' ').TrimEnd(' ', '.');

            if (clean.Length == 0)
                clean = Fallback;

            if (IsReservedWindowsName(clean))
                clean = "_" + clean;

            var ext = CleanExtension(extension);
            if (ext.Length > 0 && !HasExtension(clean))
                clean = $"{clean}.{ext}";

            return Shorten(clean);
        }


        private static string Clean(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            var sb = new StringBuilder(value.Length);
            foreach (var c in value)
            {
                var category = char.GetUnicodeCategory(c);

                // Control characters and format characters (bidi overrides, zero-width marks) can hide the real name.
                if (category is UnicodeCategory.Control or UnicodeCategory.Format)
                    continue;

                sb.Append(c);
            }

            return sb.ToString();
        }

        private static string CleanExtension(string extension)
        {
            var ext = Clean(extension);

            var lastSeparator = ext.LastIndexOfAny(['/', '\\']);
            if (lastSeparator >= 0)
                ext = ext[(lastSeparator + 1)..];

            ext = ReplaceInvalid(ext).Trim(' ').Trim('.');

            if (ext.Length > MaxExtensionLength || ext.Contains(' '))
                return string.Empty;

            return ext;
        }

        private static string ReplaceInvalid(string value)
        {
            if (value.IndexOfAny(InvalidChars.ToCharArray()) < 0)
                return value;

            var chars = value.ToCharArray();
            for (var i = 0; i < chars.Length; i++)
                if (InvalidChars.Contains(chars[i]))
                    chars[i] = '_';

            return new string(chars);
        }

        private static bool HasExtension(string name)
        {
            var dot = name.LastIndexOf('.');

            return dot > 0 && dot < name.Length - 1;
        }

        private static bool IsReservedWindowsName(string name)
        {
            var dot = name.IndexOf('.');
            var stem = (dot < 0 ? name : name[..dot]).TrimEnd(' ');

            return Array.Exists(_reservedWindowsNames, reserved => string.Equals(reserved, stem, StringComparison.OrdinalIgnoreCase));
        }

        // Cuts the stem and keeps the extension, without splitting a surrogate pair.
        private static string Shorten(string name)
        {
            if (name.Length <= MaxLength)
                return name;

            var dot = name.LastIndexOf('.');
            var suffix = dot > 0 && name.Length - dot <= MaxExtensionLength + 1 ? name[dot..] : string.Empty;
            var stemLength = MaxLength - suffix.Length;

            if (char.IsHighSurrogate(name[stemLength - 1]))
                stemLength--;

            return name[..stemLength].TrimEnd(' ', '.') + suffix;
        }

        private static bool IsAsciiLetter(char c) => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
    }
}
