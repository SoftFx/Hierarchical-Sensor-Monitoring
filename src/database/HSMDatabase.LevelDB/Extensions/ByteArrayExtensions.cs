using System.Text;

namespace HSMDatabase.LevelDB.Extensions
{
    internal static class ByteArrayExtensions
    {
        internal static bool StartsWith(this byte[] initialArray, byte[] startCheck)
        {
            for (int i = 0; i < startCheck.Length; i++)
                if (initialArray[i] != startCheck[i])
                    return false;

            return true;
        }

        internal static bool IsGreaterOrEquals(this byte[] initialBytes, byte[] anotherBytes)
        {
            if (initialBytes.Length != anotherBytes.Length)
                return initialBytes.Length > anotherBytes.Length;

            for (int i = 0; i < initialBytes.Length; ++i)
            {
                var cmpResult = initialBytes[i].CompareTo(anotherBytes[i]);

                if (cmpResult != 0)
                    return cmpResult > 0;
            }

            return true;
        }

        internal static bool IsGreater(this byte[] initialBytes, byte[] anotherBytes)
        {
            if (initialBytes.Length != anotherBytes.Length)
                return initialBytes.Length > anotherBytes.Length;

            for (int i = 0; i < initialBytes.Length; ++i)
            {
                var cmpResult = initialBytes[i].CompareTo(anotherBytes[i]);

                if (cmpResult != 0)
                    return cmpResult > 0;
            }

            return false;
        }

        internal static bool IsSmallerOrEquals(this byte[] initialBytes, byte[] anotherBytes)
        {
            if (initialBytes.Length != anotherBytes.Length)
                return initialBytes.Length < anotherBytes.Length;

            for (int i = 0; i < initialBytes.Length; ++i)
            {
                var cmpResult = initialBytes[i].CompareTo(anotherBytes[i]);

                if (cmpResult != 0)
                    return cmpResult < 0;
            }

            return true;
        }

        internal static bool IsSmaller(this byte[] initialBytes, byte[] anotherBytes)
        {
            if (initialBytes.Length != anotherBytes.Length)
                return initialBytes.Length < anotherBytes.Length;

            for (int i = 0; i < initialBytes.Length; ++i)
            {
                var cmpResult = initialBytes[i].CompareTo(anotherBytes[i]);

                if (cmpResult != 0)
                    return cmpResult < 0;
            }

            return false;
        }

        internal static string GetString(this byte[] bytes) => Encoding.UTF8.GetString(bytes);
    }
}
