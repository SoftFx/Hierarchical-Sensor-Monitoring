using System.Text;

namespace HSMDatabase.LevelDB.Extensions
{
    internal static class ByteArrayExtensions
    {
        internal static bool StartsWith(this byte[] initialArray, byte[] startCheck)
        {
            for (int i = 0; i < startCheck.Length; i++)
            {
                if (initialArray[i] != startCheck[i])
                {
                    return false;
                }
            }

            return true;
        }

        internal static bool IsGreaterOrEquals(this byte[] initialArray, byte[] anotherBytes)
        {
            if (initialArray.Length != anotherBytes.Length)
                return initialArray.Length.CompareTo(anotherBytes.Length) >= 0;


            for (int i = 0; i < initialArray.Length; ++i)
            {
                var cmpResult = initialArray[i].CompareTo(anotherBytes[i]);
                if (cmpResult != 0)
                    return cmpResult > 0;
            }

            return true;
        }

        internal static bool IsSmallerOrEquals(this byte[] initialArray, byte[] anotherBytes)
        {
            if (initialArray.Length != anotherBytes.Length)
                return initialArray.Length.CompareTo(anotherBytes.Length) > 0;

            for (int i = 0; i < initialArray.Length; ++i)
            {
                var cmpResult = initialArray[i].CompareTo(anotherBytes[i]);
                if (cmpResult != 0)
                    return cmpResult < 0;
            }

            return true;
        }

        internal static string GetString(this byte[] bytes) => Encoding.UTF8.GetString(bytes);
    }
}
