﻿using System;

namespace HSMCommon.Constants
{
    public class ConfigurationConstants
    {
        public const int SensorsPort = 44330;
        public const int SitePort = 44333;

        #region Default config

        public const int DefaultMaxPathLength = 10;
        public static readonly TimeSpan DefaultExpirationTime = new TimeSpan(30,0,0,0);

        public const string DefaultSMTPServer = "smtp.gmail.com";
        public const string DefaultSMTPPort = "";
        public const string DefaultSMTPLogin = "testEmail44543@gmail.com";
        public const string DefaultSMTPPassword = "TestEmail4";
        public const string DefaultSMTPFromEmail = "testEmail44543@gmail.com";

        public const string DefaultBotToken = "";
        public const string DefaultBotName = "";
        public const string DefaultAreBotMessagesEnabled = "False";

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

        public const string BotToken = nameof(BotToken);
        public const string BotName = nameof(BotName);
        public const string AreBotMessagesEnabled = nameof(AreBotMessagesEnabled);

        #endregion

        #region Configuration descriptions

        public const string MaxPathLengthDescription =
            "Maximum nodes amount in the sensor path (maximum slash symbols amount).";

        public const string AesEncryptionKeyDescription =
            "Encryption key for invitation links, generated automatically.";

        public const string SensorExpirationTimeDescription =
            "Sensor values older than specified period are removed. Format is dd.hh:mm:ss";

        public const string ServerCertificatePasswordDescription = "Password for server certificate, it is applied when server starts.";
        public const string SMTPServerDescription = "SMTP server name for sending invite emails.";
        public const string SMTPPortDescription = "SMTP server port for sending invite emails.";
        public const string SMTPLoginDescription = "Mail account login for sending invite emails.";
        public const string SMTPPasswordDescription = "Mail account password for sending invite emails.";
        public const string SMTPFromEmailDescription = "Mail account to send invite emails from.";

        public const string BotTokenDescription = $"Generated by BotFather secret access token. {TelegramWiki}";
        public const string BotNameDescription = $"Installed by BotFather botname. It must be ended with 'bot'. {TelegramWiki}";
        public const string AreBotMessagesEnabledDescription = "Can bot send messages.";

        private const string TelegramWiki = "Installation link <a href='https://core.telegram.org/bots/features#botfather' target='_blank'>Bot Installation</a>.";

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
                SMTPFromEmail => DefaultSMTPFromEmail,

                BotToken => DefaultBotToken,
                BotName => DefaultBotName,
                AreBotMessagesEnabled => DefaultAreBotMessagesEnabled,

                _ => string.Empty
            };
        }

        public static string GetDescription(string name)
        {
            return name switch
            {
                MaxPathLength => MaxPathLengthDescription,
                SensorExpirationTime => SensorExpirationTimeDescription,
                ServerCertificatePassword => ServerCertificatePasswordDescription,
                AesEncryptionKey => AesEncryptionKeyDescription,

                SMTPServer => SMTPServerDescription,
                SMTPPort => SMTPPortDescription,
                SMTPLogin => SMTPLoginDescription,
                SMTPPassword => SMTPPasswordDescription,
                SMTPFromEmail => SMTPFromEmailDescription,

                BotToken => BotTokenDescription,
                BotName => BotNameDescription,
                AreBotMessagesEnabled => AreBotMessagesEnabledDescription,

                _ => string.Empty
            };
        }
    }
}
