using System;
using System.Threading;

namespace HSMServer.Core.Tests.Infrastructure
{
    internal static class RandomGenerator
    {
        private const string PossibleChars =
            "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

        private static readonly ThreadLocal<Random> _random = new(() => new Random(134134278));


        internal static bool GetRandomBool() => _random.Value.Next(0, 2) > 0;

        internal static int GetRandomInt(int min = -100, int max = 100, bool positive = false) =>
            _random.Value.Next(positive ? 0 : min, max);

        internal static byte GetRandomByte(byte min = 0, byte max = 7) =>
            (byte)_random.Value.Next(min, max);

        internal static double GetRandomDouble() =>
            _random.Value.NextDouble() * (GetRandomBool() ? -100 : 100);

        internal static string GetRandomString(int size = 8)
        {
            var stringChars = new char[size];

            for (int i = 0; i < size; i++)
                stringChars[i] = PossibleChars[_random.Value.Next(PossibleChars.Length)];

            return new string(stringChars);
        }

        internal static byte[] GetRandomBytes(int size = 8)
        {
            var bytes = new byte[size];

            for (int i = 0; i < size; i++)
                bytes[i] = (byte)_random.Value.Next(0, 255);

            return bytes;
        }
    }
}
