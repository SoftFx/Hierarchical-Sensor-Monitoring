using System.IO;

namespace HSMDatabase.Extensions
{
    internal static class DirectoryExtensions
    {
        internal static long GetSize(this string directory) => new DirectoryInfo(directory).GetSize();


        internal static long GetSize(this DirectoryInfo directory)
        {
            var size = 0L;

            foreach (var file in directory.GetFiles())
            {
                try
                {
                    size += file.Length;
                }
                catch { }
            }

            foreach (var dir in directory.GetDirectories())
            {
                size += dir.GetSize();
            }

            return size;
        }
    }
}
