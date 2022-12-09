using System;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using HSMServer.Core.Model;

namespace HSMServer.Core.Extensions
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

        public static KeyState GetInversed(this KeyState state)
        {
            return state == KeyState.Blocked ? KeyState.Active : KeyState.Blocked;
        }
    }
}
