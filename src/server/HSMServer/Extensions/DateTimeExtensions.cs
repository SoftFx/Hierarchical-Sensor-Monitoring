using System;

namespace HSMServer.Extensions
{
    public static class DateTimeExtensions
    {
        private const string DateTimeDefaultFormat = "dd/MM/yyyy HH:mm:ss";


        public static string ToDefaultFormat(this DateTime dateTime) => dateTime.ToString(DateTimeDefaultFormat);

        internal static DateTime ToUtc(this DateTime dateTime) => DateTime.SpecifyKind(dateTime, DateTimeKind.Utc);
    }
}
