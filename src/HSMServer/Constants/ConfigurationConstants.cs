using System;

namespace HSMServer.Constants
{
    internal class ConfigurationConstants
    {
        public const int GrpcPort = 22900;
        public const int SensorsPort = 44330;
        public const int ApiPort = 44333;

        #region Default config

        public const int DefaultMaxPathLength = 10;
        public static readonly TimeSpan DefaultExpirationTime = new TimeSpan(30,0,0,0);

        public const string DefaultSMTPServer = "smtp.gmail.com";
        public const string DefaultSMTPPort = "";
        public const string DefaultSMTPLogin = "testEmail44543@gmail.com";
        public const string DefaultSMTPPassword = "TestEmail4";
        public const string DefaultSMTPFromEmail = "testEmail44543@gmail.com";

        #endregion

        #region Configuration Names

        public const string MaxPathLength = nameof(MaxPathLength);
        public const string AesEncryptionKey = nameof(AesEncryptionKey);
        public const string SensorExpirationTime = nameof(SensorExpirationTime);
        public const string ServerCertificatePassword = nameof(ServerCertificatePassword);

        public const string SMTPServer = nameof(SMTPServer);
        public const string SMTPPort = nameof(SMTPPort);
        public const string SMTPLogin = nameof(SMTPLogin);
        public const string SMTPPassword = nameof(SMTPPassword);
        public const string SMTPFromEmail = nameof(SMTPFromEmail);

        #endregion

        public static string GetDefault(string name)
        {
            return name switch
            {
                MaxPathLength => DefaultMaxPathLength.ToString(),
                SensorExpirationTime => DefaultExpirationTime.ToString(),
                ServerCertificatePassword => string.Empty,
                AesEncryptionKey => string.Empty,

                SMTPServer => DefaultSMTPServer,
                SMTPPort => DefaultSMTPPort,
                SMTPLogin => DefaultSMTPLogin,
                SMTPPassword => DefaultSMTPPassword,
                SMTPFromEmail => DefaultSMTPFromEmail
            };
        }
    }
}
