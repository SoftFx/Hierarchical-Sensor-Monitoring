namespace HSMServer.Notifications.Chats
{
    public enum ChatConnectOutcome
    {
        // Existing folder-scoped flow: a brand-new chat was created and bound to the folder
        // named in `ChatConnectResult.Name`.
        FolderAdded,

        // EditChat flow: Telegram was bound in-place to an existing Chat record named `Name`.
        ChatBound,

        // Conflict refused because the incoming Telegram chat is already bound to another Chat
        // record. `Name` carries the owner chat's name so the bot reply can point the user at
        // the record they need to remove first.
        FailedAlreadyBound,

        // Token invalid/expired, target chat doesn't exist, or target record already has a
        // different Telegram binding (rebind refused).
        Failed,
    }

    public sealed record ChatConnectResult(ChatConnectOutcome Outcome, string Name = null);
}
