using HSMServer.Model.Authentication;
using System;

namespace HSMServer.Notifications
{
    internal readonly struct InvitationToken
    {
        private const int TokenExpirationMinutes = 2;

        internal static InvitationToken Empty { get; } = new();


        internal User User { get; }

        internal Guid Token { get; }

        internal DateTime ExpirationTime { get; }


        internal InvitationToken(Guid folderId, User user)
        {
            User = user;
            Token = folderId;
            ExpirationTime = DateTime.UtcNow.AddMinutes(TokenExpirationMinutes);
        }
    }
}
