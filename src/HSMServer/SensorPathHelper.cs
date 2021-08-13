using System;
using System.Text;

namespace HSMServer
{
    public static class SensorPathHelper
    {
        public static string Encode(string path)
        {
            //return HttpUtility.UrlEncode(Convert.ToBase64String(
            //Encoding.Unicode.GetBytes(path)));
            return (Convert.ToBase64String(Encoding.Unicode.GetBytes(path)))
                .Replace('=', '_').Replace('+', '-').Replace(':', '/');
        }

        public static string Decode(string path)
        {
            //return Encoding.Unicode.GetString(Convert.FromBase64String(
            //HttpUtility.UrlDecode(path)));
            path = path.Replace(':', '/').Replace('-', '+').Replace('_', '=');
            return Encoding.Unicode.GetString(Convert.FromBase64String(path));
        }
    }
}
