using System;

namespace HSMServer.Core.Attributes
{
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
    public class GroupAttribute(int groupPriority, string groupName, int numberInGroup = 0) : Attribute
    {
        public int Priority { get; } = groupPriority;
        public string GroupName { get; } = groupName;
        public int NumberInGroup { get; } = numberInGroup;
    }
}
