using System;
using System.Runtime.InteropServices;

namespace HSMDataCollector.LargeFiles
{
    /// <summary>
    /// The file name mask of a watched folder, the same rule in both collectors (#1888, rule 10 of CLAUDE.md: one matcher, not
    /// one per platform library): <c>*</c> matches any run of characters (also none), <c>?</c> exactly one, every other
    /// character itself; the whole name must match. Case-insensitive on Windows by ASCII folding only (A-Z = a-z), exact
    /// elsewhere. The system's matchers are not used: they differ between net472 (8.3 names: <c>*.dmp</c> matches
    /// <c>a.dmp_old</c>), net8.0 and the native collector.
    /// </summary>
    internal sealed class FileNameMask
    {
        private readonly string _mask;
        private readonly bool _ignoreAsciiCase;

        internal FileNameMask(string mask, bool ignoreAsciiCase)
        {
            _mask = mask ?? throw new ArgumentNullException(nameof(mask));
            _ignoreAsciiCase = ignoreAsciiCase;
        }

        internal static FileNameMask ForThisSystem(string mask) => new FileNameMask(mask, RuntimeInformation.IsOSPlatform(OSPlatform.Windows));

        internal string Text => _mask;

        /// <summary>Greedy wildcard match with one backtrack point per <c>*</c> (linear in practice, no recursion).</summary>
        internal bool Matches(string name)
        {
            if (name == null)
                return false;

            int n = 0, m = 0, starMask = -1, starName = 0;

            while (n < name.Length)
            {
                if (m < _mask.Length && _mask[m] == '*')
                {
                    starMask = m++;
                    starName = n;
                }
                else if (m < _mask.Length && (_mask[m] == '?' || Same(_mask[m], name[n])))
                {
                    m++;
                    n++;
                }
                else if (starMask >= 0)
                {
                    m = starMask + 1;
                    n = ++starName;
                }
                else
                {
                    return false;
                }
            }

            while (m < _mask.Length && _mask[m] == '*')
                m++;

            return m == _mask.Length;
        }

        private bool Same(char a, char b) => a == b || (_ignoreAsciiCase && Fold(a) == Fold(b));

        private static char Fold(char c) => c >= 'A' && c <= 'Z' ? (char)(c + ('a' - 'A')) : c;
    }
}
