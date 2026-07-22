using HSMServer.Model.Authentication;
using System;

namespace HSMServer.Notifications.Telegram.Tokens
{
    public readonly struct InvitationToken
    {
        private const int TokenExpirationMinutes = 2;

        internal static InvitationToken Empty { get; } = new();


        internal User User { get; }

        internal Guid Token { get; }

        // Set when the invite targets an existing Chat record (EditChat flow) — TryConnect binds
        // Telegram in place. Null for the folder-scoped flow, where a brand-new chat is created.
        internal Guid? ChatId { get; }

        internal Guid FolderId { get; }

        internal DateTime ExpirationTime { get; }


        internal InvitationToken(Guid folderId, User user) : this(null, folderId, user) { }

        internal InvitationToken(Guid? chatId, Guid folderId, User user)
        {
            User = user;
            ChatId = chatId;
            FolderId = folderId;
            Token = Guid.NewGuid();
            ExpirationTime = DateTime.UtcNow.AddMinutes(TokenExpirationMinutes);
        }
    }
}
