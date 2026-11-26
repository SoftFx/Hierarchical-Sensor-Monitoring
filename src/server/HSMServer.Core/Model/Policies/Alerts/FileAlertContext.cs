using System;
using System.Text;
using HSMCommon.Model;
using HSMServer.Core.Extensions;

namespace HSMServer.Core.Model.Policies
{
    /// <summary>
    /// What the alert variables of a File sensor (#1886) need from the server and the Core does not hold: the address of
    /// the sensor page and whether a product's large files wait for its dump password.
    /// </summary>
    public interface IFileAlertContext
    {
        /// <summary>The link to the sensor's page: absolute when the server knows its public address, else the path alone.</summary>
        string SensorPageLink(Guid sensorId);

        /// <summary>
        /// True when the file is a secure large file of a product without a dump password: kept, and not downloadable
        /// until a password is set. A not secure file never waits for one.
        /// </summary>
        bool WaitsForDumpPassword(Guid productId, Guid fileId);
    }


    /// <summary>
    /// The one <see cref="IFileAlertContext"/> of the process, set by the server at start before the tree loads. Every File
    /// policy reads it whatever created the policy (a registration, a load, a template, the management API, a move), so no
    /// path leaves <c>$link</c> empty. Until it is set (a test, a tool), <c>$link</c> is the path of the page.
    /// </summary>
    public static class FileAlertContext
    {
        /// <summary>The page of a sensor in the web UI, relative to the server's address.</summary>
        public static string SensorPagePath(Guid sensorId) => $"/Home/{sensorId}";

        private static volatile IFileAlertContext _current = PathOnly.Instance;


        public static IFileAlertContext Current => _current;

        public static void Use(IFileAlertContext context) => _current = context ?? PathOnly.Instance;


        private sealed class PathOnly : IFileAlertContext
        {
            internal static readonly PathOnly Instance = new();

            public string SensorPageLink(Guid sensorId) => SensorPagePath(sensorId);

            public bool WaitsForDumpPassword(Guid productId, Guid fileId) => false;
        }
    }


    /// <summary>The values of the File sensor alert variables.</summary>
    public static class FileAlertVariables
    {
        /// <summary>The longest file name a message carries; a longer one is cut, so a message keeps its link.</summary>
        public const int MaxFileNameLength = 200;

        public const string WaitsForPasswordNote = "waits for the product's dump password";


        /// <summary>
        /// The file name for a message. A name is the client's text and a message is rendered as markup on the way (the
        /// web comment, Telegram, which honours tags and HTML entities): angle brackets and the ampersand are replaced by
        /// look-alikes, line breaks, control and format characters (a right-to-left override) by a space; then it is cut to
        /// <see cref="MaxFileNameLength"/>.
        /// </summary>
        public static string FileName(FileValue file)
        {
            var name = file.GetDisplayName();

            if (string.IsNullOrEmpty(name))
                return string.Empty;

            var safe = new StringBuilder(Math.Min(name.Length, MaxFileNameLength + 1));

            // By rune, so a format character outside the BMP (a surrogate pair) is seen as one.
            foreach (var rune in name.EnumerateRunes())
            {
                var category = Rune.GetUnicodeCategory(rune);

                if (rune.Value == '<')
                    safe.Append('\u2039');
                else if (rune.Value == '>')
                    safe.Append('\u203A');
                else if (rune.Value == '&')
                    safe.Append('\uFF06');
                else if (category is System.Globalization.UnicodeCategory.Control or System.Globalization.UnicodeCategory.Format
                         or System.Globalization.UnicodeCategory.LineSeparator or System.Globalization.UnicodeCategory.ParagraphSeparator)
                    safe.Append(' ');
                else
                    safe.Append(rune.ToString());

                if (safe.Length > MaxFileNameLength)
                    break;
            }

            if (safe.Length > MaxFileNameLength)
            {
                safe.Length = MaxFileNameLength - 1;

                if (char.IsHighSurrogate(safe[^1]))
                    safe.Length--;

                safe.Append('\u2026');
            }

            return safe.ToString();
        }

        public static string FileNote(FileValue file, BaseSensorModel sensor) =>
            file.IsLargeFile() && file.LargeFileIdOrDefault() is { } fileId && sensor?.Root is { } product &&
            FileAlertContext.Current.WaitsForDumpPassword(product.Id, fileId)
                ? WaitsForPasswordNote
                : string.Empty;
    }
}
