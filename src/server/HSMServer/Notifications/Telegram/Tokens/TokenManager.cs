using System;
using System.Collections.Concurrent;

namespace HSMServer.Notifications.Telegram.Tokens
{
    public sealed class TokenManager
    {
        private readonly ConcurrentDictionary<Guid, InvitationToken> _tokens = new();


        internal Guid BuildInvitationToken(Guid folderId, Model.Authentication.User user)
        {
            var invitationToken = new InvitationToken(folderId, user);

            _tokens[invitationToken.Token] = invitationToken;

            return invitationToken.Token;
        }

        internal bool TryRemoveToken(string tokenIdStr, out InvitationToken token)
        {
            token = InvitationToken.Empty;

            return Guid.TryParse(tokenIdStr, out var tokenId) && _tokens.TryRemove(tokenId, out token);
        }

        internal void RemoveOldTokens()
        {
            foreach (var (tokenId, token) in _tokens)
                if (DateTime.UtcNow >= token.ExpirationTime.AddHours(1))
                    _tokens.TryRemove(tokenId, out _);
        }
    }
}
