using System;
using System.Drawing;

namespace HSMServer.Extensions
{
    public static class ColorExtensions
    {
        public static Color ToSuitableFont(this Color color)
        {
            double luminance = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255;
            int d = luminance > 0.5 ? 0 : 255;

            return Color.FromArgb(d, d, d);
        }

        public static string ToRGB(this Color color) => ColorTranslator.ToHtml(color);

        public static string ToRGBA(this Color color, double alpha = 0.7) => $"rgba({color.R},{color.G},{color.B},{alpha})";


        public static string GenerateRandomColor()
        {
            const int maxHex = 16777215; // FFFFFF number

            var randomHex = $"{(int)Math.Floor(Random.Shared.NextDouble() * maxHex):x}";

            return $"#{randomHex.PadLeft(6, '0')}";
        }
    }
}
