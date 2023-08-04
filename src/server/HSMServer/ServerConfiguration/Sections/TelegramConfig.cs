namespace HSMServer.ServerConfiguration
{
    public class TelegramConfig
    {
        internal bool IsValid => !string.IsNullOrEmpty(BotName) && !string.IsNullOrEmpty(BotToken);


        public string BotName { get; set; }

        public string BotToken { get; set; }

        public bool IsRunning { get; set; }
    }
}