using System.Globalization;
using System.Text;
using HSMDatabase.SealedHistory.Format;

namespace HSMDatabase.SealedHistory.Tests
{
    /// <summary>
    /// Strings built from templates and numeric shapes, for the string column template mode (mode 2): ordinary
    /// numbers next to every corner the tokenizer and the decoder bounds have. Deterministic for a given Random.
    /// All texts are made up.
    /// </summary>
    internal static class StringShapes
    {
        private static readonly string[] Words = ["queue", "load", "temp", "id", "", " ", "x", "Ошибка", "東京", "\U0001F600", "a-b", "v", "-", ".", "--", "+"];

        private static readonly string[] Corners =
        [
            "0", "-0", "-0.0", "-000.1", "0.0", "00.5", "007", "1.", ".5", "1..2", "1.2.3", "10.0.0.1", "--3", "+5", "-5", "a-5", "5-3",
            "12345678901234567", "123456789012345678", "1234567890123456789", "12345678901234567890",
            "9223372036854775807", "9223372036854775808", "-9223372036854775807", "-9223372036854775808",
            "0.12345678901234567", "1.12345678901234567", "12345678901234567.8", "123456789012345678.9", "0.000000000000000000000001",
            "١٢٣", "１２", "०", "1٢", "²", "1e-5", "1,5", "-.5", "+.5", "0x1F",
        ];


        /// <summary>A string drawn from one of the template shapes, the corners included.</summary>
        public static string Random(Random random) => random.Next(10) switch
        {
            0 => Corners[random.Next(Corners.Length)],
            1 => Number(random),
            2 => Joined(random, random.Next(1, 4)),
            3 => Zeros(random) ,
            4 => Fraction(random),
            5 => Many(random),
            _ => Joined(random, random.Next(0, 7)),
        };

        /// <summary>
        /// A column of n strings in one of the column styles: a few templates with varying numbers, every row a
        /// different template, nulls and empties mixed in, zero-slot strings, or anything.
        /// </summary>
        public static string[] Column(Random random, int n)
        {
            var column = new string[n];
            int style = random.Next(6);
            var templates = Enumerable.Range(0, random.Next(1, 6)).Select(_ => Words[random.Next(Words.Length)] + " " + random.Next(1000)).ToArray();

            for (int i = 0; i < n; i++)
            {
                column[i] = style switch
                {
                    0 => $"{templates[i % templates.Length]} {Number(random)} of {Number(random)}",
                    1 => $"{Letters(i)} {Number(random)}",                                   // every row a different template
                    2 => random.Next(4) switch { 0 => null, 1 => "", _ => Random(random) },
                    3 => random.Next(3) == 0 ? templates[0] : $"{templates[0]} {random.Next(10)}", // zero-slot strings mixed in
                    4 => i % 2 == 0 ? Corners[i % Corners.Length] : Number(random),
                    _ => Random(random),
                };
            }

            return column;
        }

        private static string Number(Random random) => random.Next(9) switch
        {
            0 => random.Next(-1000, 1000).ToString(CultureInfo.InvariantCulture),
            1 => random.NextInt64(long.MinValue, long.MaxValue).ToString(CultureInfo.InvariantCulture),
            2 => (random.NextDouble() * 1000 - 500).ToString("F" + random.Next(0, 6), CultureInfo.InvariantCulture),
            3 => random.Next(100).ToString("D" + random.Next(1, 8), CultureInfo.InvariantCulture),
            4 => (random.Next(2) == 0 ? "-" : "") + random.Next(10) + "." + new string('0', random.Next(0, 3)) + random.Next(1000),
            5 => new string((char)('0' + random.Next(10)), random.Next(16, 22)),
            6 => Zeros(random),
            7 => Fraction(random),
            _ => random.Next(10).ToString(CultureInfo.InvariantCulture),
        };

        /// <summary>Runs with Z at and around its cap.</summary>
        private static string Zeros(Random random)
        {
            int zeros = StringTemplate.MaxLeadingZeros + random.Next(-2, 3);
            return new string('0', zeros + 1) + (random.Next(2) == 0 ? "" : random.Next(1, 100).ToString(CultureInfo.InvariantCulture)) +
                   (random.Next(2) == 0 ? "" : "." + random.Next(100).ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>Fractions with F at and around its cap, and around 18 significant digits.</summary>
        private static string Fraction(Random random)
        {
            int f = random.Next(2) == 0 ? StringTemplate.MaxFractionDigits + random.Next(-2, 3) : random.Next(15, 21);
            var fraction = new StringBuilder();
            for (int k = 0; k < f; k++)
                fraction.Append((char)('0' + (k < f / 2 && random.Next(2) == 0 ? 0 : random.Next(10))));
            return (random.Next(3) == 0 ? "-" : "") + random.Next(3) switch { 0 => "0", 1 => "00", _ => random.Next(1, 1000).ToString(CultureInfo.InvariantCulture) } + "." + fraction;
        }

        /// <summary>A string with more runs than a template may have slots.</summary>
        private static string Many(Random random)
        {
            int runs = StringTemplate.MaxSlotsPerTemplate + random.Next(-2, 4);
            return string.Join(",", Enumerable.Range(0, runs).Select(k => random.Next(2) == 0 ? k.ToString(CultureInfo.InvariantCulture) : "-" + k));
        }

        private static string Joined(Random random, int parts)
        {
            var builder = new StringBuilder();
            for (int p = 0; p < parts; p++)
            {
                builder.Append(Words[random.Next(Words.Length)]);
                builder.Append(random.Next(4) == 0 ? "" : " ");
                builder.Append(random.Next(5) == 0 ? Corners[random.Next(Corners.Length)] : Number(random));
            }
            builder.Append(Words[random.Next(Words.Length)]);
            return builder.ToString();
        }

        private static string Letters(int i)
        {
            var builder = new StringBuilder("t");
            do
            {
                builder.Append((char)('a' + i % 26));
                i /= 26;
            } while (i > 0);
            return builder.ToString();
        }
    }
}
