namespace HSMServer.Notifications
{
    /// <summary>
    /// What Telegram answered when the server asked who owns a bot token (#1830): the bot's
    /// username, a refusal (the token is not valid), or no answer at all (Telegram could not
    /// be reached).
    /// </summary>
    public enum TelegramTokenVerdict
    {
        Accepted,

        Rejected,

        Unreachable,
    }


    /// <summary>
    /// The answer to one token probe. <see cref="Reason"/> is the text of a refusal or of a
    /// failure, with the token wiped — never the token itself.
    /// </summary>
    public sealed record TelegramTokenProbe(TelegramTokenVerdict Verdict, string Username, string Reason)
    {
        public static TelegramTokenProbe Accepted(string username) => new(TelegramTokenVerdict.Accepted, username, null);

        public static TelegramTokenProbe Rejected(string reason) => new(TelegramTokenVerdict.Rejected, null, reason);

        public static TelegramTokenProbe Unreachable(string reason) => new(TelegramTokenVerdict.Unreachable, null, reason);
    }
}
