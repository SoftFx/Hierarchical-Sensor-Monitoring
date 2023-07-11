namespace HSMServer.Model.UserTreeShallowCopy
{
    public sealed class GroupNotificationsState
    {
        public required string Name { get; init; }

        public bool IsEnabled { get; set; } = true;

        public bool IsIgnored { get; set; } = true;


        internal void CalculateState(GroupNotificationsState groupInfo)
        {
            IsEnabled &= groupInfo.IsEnabled;
            IsIgnored &= groupInfo.IsIgnored;
        }
    }
}
