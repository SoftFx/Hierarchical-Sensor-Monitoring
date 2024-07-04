using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace HSMCommon.Extensions
{
    public static class ObjectExtensions
    {
        public static string GetDisplayName(this object obj, string propertyName)
        {
            if (obj == null)
                return null;

            var property = obj.GetType().GetProperty(propertyName);
            if (property == null)
                return null;

            return property.GetCustomAttribute<DisplayAttribute>()?.Name ?? property.Name;
        }
    }
}
