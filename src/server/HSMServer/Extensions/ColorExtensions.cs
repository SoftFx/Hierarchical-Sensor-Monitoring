using System.Drawing;

namespace HSMServer.Extensions
{
    public static class ColorExtensions
    {
        public static string ToRGB(this Color color) => ColorTranslator.ToHtml(color);

        public static string ToRGBA(this Color color, double alpha) => $"rgba({color.R},{color.G},{color.B},{alpha})";
    }
}
