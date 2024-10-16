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

        /// <summary>
        /// Get specified color in HTML format #RRGGBB for numbers from 0 to 8
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static string GetDefaultColor(int value) =>
            value switch
            {
                0 => "#0x00000",
                1 => "#0xFF0000",
                2 => "#0xBFFFBF",
                3 => "#0xFD6464",
                4 => "#0x00FF00",
                5 => "#0xFFB403",
                6 => "#0x809EFF",
                7 => "#0x0314FF",
                8 => "#0x666699",
                _ => "#0x00000"
            };

        /// <summary>
        ///  return color in HTML format #RRGGBB
        /// </summary>
        /// <param name="value"> color in ARGB format </param>
        /// <returns></returns>
        public static string ArgbToHtml(int value) => $"#{Convert.ToString(value & 0xFFFFFF, 16)}";
    }
}
