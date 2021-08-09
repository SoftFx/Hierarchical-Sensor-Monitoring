using System;

namespace HSMDatabase.LevelDB.Extensions
{
    public static class ByteArrayExtensions
    {
        public static bool StartsWith(this byte[] initialArray, byte[] startCheck)
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

        public static bool IsGreater(this byte[] initialArray, byte[] anotherBytes)
        {
            for (int i = 0; i < Math.Min(initialArray.Length, anotherBytes.Length); ++i)
            {
                var cmpResult = initialArray[i].CompareTo(anotherBytes[i]);
                if (cmpResult != 0)
                    return cmpResult > 0;
            }

            return initialArray.Length.CompareTo(anotherBytes.Length) > 0;
        }
    }
}
