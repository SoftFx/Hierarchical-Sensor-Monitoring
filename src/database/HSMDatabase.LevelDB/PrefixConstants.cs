using System;

namespace HSMDatabase.LevelDB
{
    public static class PrefixConstants
    {
        private const string USER_INFO_PREFIX = "UserInfo";
        private const string CONFIGURATION_OBJECT_PREFIX = "ConfigurationObject";
        private const string REGISTRATION_TICKET_PREFIX = "RegistrationTicket";


        internal static string GetUniqueUserKey(string userName)
        {
            return $"{USER_INFO_PREFIX}_{userName}";
        }

        internal static string GetUsersReadKey()
        {
            return USER_INFO_PREFIX;
        }

        internal static string GetUniqueConfigurationObjectKey(string objectName)
        {
            return $"{CONFIGURATION_OBJECT_PREFIX}_{objectName}";
        }

        internal static string GetRegistrationTicketKey(Guid id)
        {
            return $"{REGISTRATION_TICKET_PREFIX}_{id}";
        }
    }
}

