namespace HSMServer.Extensions
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
    }
}
