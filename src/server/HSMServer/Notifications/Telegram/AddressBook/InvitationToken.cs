using HSMServer.Core.Model;
using System;

namespace HSMServer.Notifications
{
    internal readonly struct InvitationToken
    {
        private const int TokenExpirationMinutes = 2;

        internal static InvitationToken Empty { get; } = new();


        internal INotificatable Entity { get; }

        internal Guid Token { get; }

        internal DateTime ExpirationTime { get; }


        internal InvitationToken(INotificatable entity)
        {
            Token = Guid.NewGuid();
            ExpirationTime = DateTime.UtcNow.AddMinutes(TokenExpirationMinutes);
            Entity = entity;
        }
    }
}
