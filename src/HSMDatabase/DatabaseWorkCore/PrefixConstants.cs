namespace HSMDatabase.DatabaseWorkCore
{
    public class PrefixConstants
    {
        public const string JOB_SENSOR_PREFIX = "JobSensorValue";
        public const string SENSOR_KEY_PREFIX = "SensorKey";
        public const string SENSOR_VALUE_PREFIX = "SensorValue";
        public const string SENSORS_LIST_PREFIX = "SensorsList";
        public const string PRODUCTS_LIST_PREFIX = "ProductsNames";
        public const string PRODUCT_INFO_PREFIX = "ProductInfo";
        public const string FIRST_LOGIN_PREFIX = "FirstLogin";
        public const string USER_INFO_PREFIX = "UserInfo";
        public const string CONFIGURATION_OBJECT_PREFIX = "ConfigurationObject";
        public const string REGISTRATION_TICKET_PREFIX = "RegistrationTicket";

        public static string GetUniqueUserKey(string userName)
        {
            return $"{USER_INFO_PREFIX}_{userName}";
        }

        public static string GetUsersReadKey()
        {
            return USER_INFO_PREFIX;
        }

        public static string GetSensorsListKey(string productName)
        {
            return $"{SENSORS_LIST_PREFIX}_{productName}";
        }

        public static string GetProductsListKey()
        {
            return PRODUCTS_LIST_PREFIX;
        }

        public static string GetProductInfoKey(string productName)
        {
            return $"{PRODUCT_INFO_PREFIX}_{productName}";
        }
    }
}
