namespace HSMDatabase.LevelDB
{
    public static class PrefixConstants
    {
        private const string USER_INFO_PREFIX = "UserInfo";


        internal static string GetUniqueUserKey(string userName)
        {
            return $"{USER_INFO_PREFIX}_{userName}";
        }

        internal static string GetUsersReadKey()
        {
            return USER_INFO_PREFIX;
        }
    }
}