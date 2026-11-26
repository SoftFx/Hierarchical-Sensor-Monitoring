using HSMServer.Model.Authentication;
using System;

namespace HSMServer.Notifications.Telegram.Tokens
{
    public readonly struct InvitationToken
    {
        private const int TokenExpirationMinutes = 2;

        // The web UI's issue-at-the-moment-of-use lifetime — the one default of the token store.
        internal static TimeSpan DefaultExpiration => TimeSpan.FromMinutes(TokenExpirationMinutes);

        internal static InvitationToken Empty { get; } = new();


        internal User User { get; }

        internal Guid Token { get; }

        // Set when the invite targets an existing Chat record (EditChat flow) — TryConnect binds
        // Telegram in place. Null for the folder-scoped flow, where a brand-new chat is created.
        internal Guid? ChatId { get; }

        internal Guid FolderId { get; }

        internal DateTime ExpirationTime { get; }


        internal InvitationToken(Guid folderId, User user) : this(null, folderId, user) { }

        internal InvitationToken(Guid? chatId, Guid folderId, User user) : this(chatId, folderId, user, DefaultExpiration) { }

        // The management-surface issuer (#1826) passes its longer relay lifetime; the web UI
        // keeps the default (it issues at the moment of use).
        internal InvitationToken(Guid? chatId, Guid folderId, User user, TimeSpan ttl)
        {
            User = user;
            ChatId = chatId;
            FolderId = folderId;
            Token = Guid.NewGuid();
            ExpirationTime = DateTime.UtcNow.Add(ttl);
        }
    }
}
