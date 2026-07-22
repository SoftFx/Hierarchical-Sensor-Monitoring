namespace HSMServer.Notifications.Chats
{
    public enum ChatConnectOutcome
    {
        // Existing folder-scoped flow: a brand-new chat was created and bound to the folder
        // named in `ChatConnectResult.Name`.
        FolderAdded,

        // EditChat flow: Telegram was bound in-place to an existing Chat record named `Name`.
        ChatBound,

        // Token invalid/expired, target chat doesn't exist, or conflict refused (target already
        // bound to a different Telegram chat, or Telegram chat already owned by another record).
        Failed,
    }

    public sealed record ChatConnectResult(ChatConnectOutcome Outcome, string Name = null);
}
