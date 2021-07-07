namespace HSMServer.Constants
{
    internal class ConfigurationConstants
    {
        public const int GrpcPort = 22900;
        public const int SensorsPort = 44330;
        public const int ApiPort = 44333;

        #region Default config

        public const int DefaultMaxPathLength = 10;

        #endregion

        #region Configuration Names

        public const string MaxPathLength = nameof(MaxPathLength);

        #endregion

        public static string GetDefault(string name)
        {
            return name switch
            {
                MaxPathLength => DefaultMaxPathLength.ToString()
            };
        }
    }
}
