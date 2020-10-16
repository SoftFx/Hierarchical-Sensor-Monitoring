using System;
using System.Text;

namespace HSMCommon.Keys
{
    public static class KeyGenerator
    {
        public static string GenerateProductKey(string productName)
        {
            //return HashComputer.ComputeSha256Hash($"{productName}_{DateTime.Now.ToShortTimeString()}");
            return Convert.ToBase64String(Encoding.ASCII.GetBytes($"{productName}_{DateTime.Now.ToShortTimeString()}_{DateTime.Now.ToShortDateString()}"));
        }
    }
}
