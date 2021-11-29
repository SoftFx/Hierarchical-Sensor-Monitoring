using System;

namespace HSMServer.Core.Tests.MonitoringDataReceiverTests
{
    internal static class RandomValues
    {
        private static readonly Random _random = new(134134278);
        private static readonly string _possibleChars =
            "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";


        internal static bool GetRandomBool() => _random.Next(0, 2) > 0;

        internal static int GetRandomInt(bool positive = false) =>
            _random.Next(positive ? 0 : -100, 100);

        internal static double GetRandomDouble() =>
            _random.NextDouble() * (GetRandomBool() ? -100 : 100);

        internal static string GetRandomString(int size = 8)
        {
            var stringChars = new char[size];

            for (int i = 0; i < size; i++)
                stringChars[i] = _possibleChars[_random.Next(_possibleChars.Length)];

            return new string(stringChars);
        }

        internal static byte[] GetRandomBytes(int size = 8)
        {
            var bytes = new byte[size];

            for (int i = 0; i < size; i++)
                bytes[i] = (byte)_random.Next(0, 255);

            return bytes;
        }
    }
}
