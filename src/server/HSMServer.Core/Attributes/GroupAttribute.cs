using System;

namespace HSMServer.Core.Attributes
{
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
    public class GroupAttribute: Attribute
    {
        public int Priority { get; set; }
        public string GroupName { get; set; }
        public int NumberInGroup { get; set; }

        public GroupAttribute(int groupPriority, string groupName, int numberInGroup = 0)
        {
            Priority = groupPriority;
            GroupName = groupName;
            NumberInGroup = numberInGroup;
        }
    }
}
