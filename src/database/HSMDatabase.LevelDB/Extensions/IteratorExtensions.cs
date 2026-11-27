using System;
using System.Runtime.InteropServices;
using LevelDB;

namespace HSMDatabase.LevelDB.Extensions
{
    /// <summary>Reads of a LevelDB iterator that the binding's <see cref="Iterator"/> does not offer (#1913).</summary>
    internal static class IteratorExtensions
    {
        /// <summary>
        /// Whether the current value starts with <paramref name="firstByte"/>, read in place: the value is not copied out
        /// of LevelDB (<see cref="Iterator.Value"/> copies all of it). The iterator must be valid.
        /// </summary>
        public static bool ValueStartsWith(this Iterator iterator, byte firstByte)
        {
            var value = LevelDBInterop.leveldb_iter_value(iterator.Handle, out var length);

            return length != IntPtr.Zero && Marshal.ReadByte(value) == firstByte;
        }

        /// <summary>
        /// Throws when the iterator stopped on an error (a corrupt block, a read error of a table file): LevelDB reports
        /// one only through the iterator's status, and an iteration that ends on it otherwise looks like the end of the data.
        /// </summary>
        public static void ThrowIfFailed(this Iterator iterator, string database)
        {
            var error = IntPtr.Zero;

            LevelDBInterop.leveldb_iter_get_error(iterator.Handle, out error);

            if (error == IntPtr.Zero)
                return;

            var message = Marshal.PtrToStringAnsi(error);
            LevelDBInterop.leveldb_free(error);

            throw new ServerDatabaseException($"A read of {database} failed: {message}");
        }
    }
}
