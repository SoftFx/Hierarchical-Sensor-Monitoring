using HSMServer.Core.Model.Authentication;
using System;

namespace HSMServer.Core.Notifications
{
    internal sealed record InvitationToken
    {
        private const int TokenExpirationMinutes = 2;


        internal User User { get; }

        internal Guid Token { get; private set; }

        internal DateTime ExpirationTime { get; private set; }

        internal bool WasSuccessfullyUsed { get; private set; }


        internal InvitationToken(User user)
        {
            Token = Guid.NewGuid();
            ExpirationTime = DateTime.UtcNow.AddMinutes(TokenExpirationMinutes);
            User = user;
        }


        internal void TagTokenAsSuccessfullyUsed()
        {
            WasSuccessfullyUsed = true;

            Token = Guid.Empty;
            ExpirationTime = DateTime.MinValue;
        }

        internal string ToLink(string botName) =>
             WasSuccessfullyUsed ? $"https://t.me/{botName}" : $"https://t.me/{botName}?start={Token}";
    }
}
