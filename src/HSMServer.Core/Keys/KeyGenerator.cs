using System;
using HSMCommon;

namespace HSMServer.Core.Keys
{
    public static class KeyGenerator
    {
        public static string GenerateProductKey(string productName)
        {
            return HashComputer.ComputeSha256Hash(
                $"{productName}_{DateTime.Now.ToLongTimeString()}").Substring(0, 30);
            //return Convert.ToBase64String(Encoding.ASCII.GetBytes($"{productName}_{DateTime.Now.ToShortTimeString()}_{DateTime.Now.ToShortDateString()}"));
        }

        public static string GenerateExtraProductKey(string productName, string extraProductName)
        {
            return HashComputer.ComputeSha256Hash(
                $"{productName}_{extraProductName}").Substring(0, 30);
        }
    }
}
