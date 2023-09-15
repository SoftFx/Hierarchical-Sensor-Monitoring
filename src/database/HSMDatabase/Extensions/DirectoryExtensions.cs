using System;
using System.IO;

namespace HSMDatabase.Extensions
{
    internal static class DirectoryExtensions
    {
        internal static long GetSize(this string directory) => new DirectoryInfo(directory).GetSize();


        internal static long GetSize(this DirectoryInfo directory)
        {
            var size = 0L;

            try
            {
                if (directory.Exists)
                {
                    foreach (var file in directory.GetFiles())
                    {
                        try
                        {
                            size += file.Length;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(ex.ToString());
                        }
                    }

                    foreach (var dir in directory.GetDirectories())
                    {
                        size += dir.GetSize();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }

            return size;
        }
    }
}
