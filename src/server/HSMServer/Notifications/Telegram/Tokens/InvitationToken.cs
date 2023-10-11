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

        internal Guid FolderId { get; }

        internal DateTime ExpirationTime { get; }


        internal InvitationToken(Guid folderId, User user)
        {
            User = user;
            FolderId = folderId;
            Token = Guid.NewGuid();
            ExpirationTime = DateTime.UtcNow.AddMinutes(TokenExpirationMinutes);
        }
    }
}
