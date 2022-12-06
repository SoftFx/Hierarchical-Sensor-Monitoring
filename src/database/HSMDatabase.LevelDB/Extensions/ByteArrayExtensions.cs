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

        public static bool IsGreaterOrEquals(this byte[] initialArray, byte[] anotherBytes)
        {
            if (initialArray.Length != anotherBytes.Length)
                return initialArray.Length.CompareTo(anotherBytes.Length) >= 0;


            for (int i = 0; i < initialArray.Length; ++i)
            {
                var cmpResult = initialArray[i].CompareTo(anotherBytes[i]);
                if (cmpResult != 0)
                    return cmpResult < 0;
            }

            return true;
        }

        public static bool IsSmallerOrEquals(this byte[] initialArray, byte[] anotherBytes)
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
    }
}
