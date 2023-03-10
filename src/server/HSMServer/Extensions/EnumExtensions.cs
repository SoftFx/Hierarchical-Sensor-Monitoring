using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;

namespace HSMServer.Extensions
{
    public static class EnumExtensions
    {
        public static string GetDisplayName(this Enum enumValue)
        {
            var enumValueStr = enumValue.ToString();

            return enumValue.GetType()
                            .GetMember(enumValueStr)
                            .First()
                            .GetCustomAttribute<DisplayAttribute>()
                            ?.Name ?? enumValueStr;
        }
    }
}
