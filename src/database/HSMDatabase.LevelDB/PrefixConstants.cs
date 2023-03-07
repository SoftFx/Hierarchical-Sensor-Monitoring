using System;

namespace HSMDatabase.LevelDB
{
    public static class PrefixConstants
    {
        private const string SENSORIDS_PREFIX = "SensorIds";
        private const string POLICYIDS_PREFIX = "PolicyIds";
        private const string PRODUCTS_LIST_PREFIX = "ProductsNames";
        private const string ACCESS_KEY_LIST_PREFIX = "AccessKeys";
        private const string USER_INFO_PREFIX = "UserInfo";
        private const string CONFIGURATION_OBJECT_PREFIX = "ConfigurationObject";
        private const string REGISTRATION_TICKET_PREFIX = "RegistrationTicket";

        internal const string JOB_SENSOR_PREFIX = "JobSensorValue";
        internal const string FIRST_LOGIN_PREFIX = "FirstLogin";


        internal static string GetUniqueUserKey(string userName)
        {
            return $"{USER_INFO_PREFIX}_{userName}";
        }

        internal static string GetUsersReadKey()
        {
            return USER_INFO_PREFIX;
        }

        internal static string GetProductsListKey()
        {
            return PRODUCTS_LIST_PREFIX;
        }

        internal static string GetAccessKeyListKey() => ACCESS_KEY_LIST_PREFIX;

        internal static string GetUniqueConfigurationObjectKey(string objectName)
        {
            return $"{CONFIGURATION_OBJECT_PREFIX}_{objectName}";
        }

        internal static string GetRegistrationTicketKey(Guid id)
        {
            return $"{REGISTRATION_TICKET_PREFIX}_{id}";
        }

        internal static string GetSensorIdsKey() => SENSORIDS_PREFIX;

        internal static string GetPolicyIdsKey() => POLICYIDS_PREFIX;
    }
}

