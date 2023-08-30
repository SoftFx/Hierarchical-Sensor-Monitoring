using System;

namespace HSMServer.Core.Extensions;

public static class DateTimeExtensions
{
    private const string DateTimeDefaultFormat = "dd/MM/yyyy HH:mm:ss";


    public static string ToDefaultFormat(this DateTime dateTime) => dateTime.ToString(DateTimeDefaultFormat);
}