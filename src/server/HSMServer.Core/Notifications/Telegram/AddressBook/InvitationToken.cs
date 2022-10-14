using HSMServer.Core.Model.Authentication;
using System;

namespace HSMServer.Core.Notifications
{
    internal readonly struct InvitationToken
    {
        private const int TokenExpirationMinutes = 2;

        internal static InvitationToken Empty { get; } = new();


        internal User User { get; }

        internal Guid Token { get; }

        internal DateTime ExpirationTime { get; }


        internal InvitationToken(User user)
        {
            Token = Guid.NewGuid();
            ExpirationTime = DateTime.UtcNow.AddMinutes(TokenExpirationMinutes);
            User = user;
        }
    }
}
