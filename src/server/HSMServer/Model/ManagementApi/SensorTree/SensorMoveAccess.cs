using System;
using System.Security.Claims;
using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.TableOfChanges;
using HSMServer.Model.Authentication;
using HSMServer.Model.ManagementApi.ChangeSets;

namespace HSMServer.Model.ManagementApi.SensorTree
{
    /// <summary>
    /// Who is moving sensors (#1722): the write decision per resource and the
    /// journal initiator. One move engine, two callers — an API token (REST,
    /// MCP) and a signed-in web user.
    /// </summary>
    public interface ISensorMoveAccess
    {
        /// <summary>Allowed, Forbidden (in sight, not writable) or NotFound (unknown or out of sight).</summary>
        ApiTokenAuthorization AuthorizeWrite(ApiTokenResource resource);

        InitiatorInfo Initiator { get; }
    }


    // The token side: the evaluator's decision, the token owner as initiator.
    internal sealed class TokenSensorMoveAccess : ISensorMoveAccess
    {
        private readonly IApiTokenAuthorizationService _authorization;
        private readonly ClaimsPrincipal _principal;
        private readonly IUserManager _users;

        public TokenSensorMoveAccess(IApiTokenAuthorizationService authorization, ClaimsPrincipal principal, IUserManager users)
        {
            _authorization = authorization;
            _principal = principal;
            _users = users;
        }

        public ApiTokenAuthorization AuthorizeWrite(ApiTokenResource resource) =>
            _authorization.AuthorizeWrite(_principal, resource);

        public InitiatorInfo Initiator => ChangeSetRecorder.InitiatorOf(_principal, _users);
    }


    // The web side: product roles are granted on ROOT products, so a sensor
    // resolves to its live root product. Out of sight (no role, not admin) is
    // NotFound; in sight without the Manager role is Forbidden — the rule
    // RemoveNode applies (#1512): moving reshapes the tree like removing does.
    // Moving across products therefore needs Manager on both roots.
    public sealed class CookieSensorMoveAccess : ISensorMoveAccess
    {
        private readonly User _user;
        private readonly ITreeValuesCache _cache;

        public CookieSensorMoveAccess(User user, ITreeValuesCache cache)
        {
            _user = user;
            _cache = cache;
        }

        public ApiTokenAuthorization AuthorizeWrite(ApiTokenResource resource)
        {
            if (_user is null)
                return ApiTokenAuthorization.NotFound;

            Guid? root = resource.Kind switch
            {
                ApiTokenResourceKind.Product => _cache.GetProduct(resource.Id) is { } product ? (product.Root ?? product).Id : null,
                ApiTokenResourceKind.Sensor => _cache.GetSensor(resource.Id)?.Root?.Id,
                _ => null,
            };

            if (root is not { } rootId || !_user.IsProductAvailable(rootId))
                return ApiTokenAuthorization.NotFound;

            return _user.IsManager(rootId) ? ApiTokenAuthorization.Allowed : ApiTokenAuthorization.Forbidden;
        }

        public InitiatorInfo Initiator => InitiatorInfo.AsUser(_user?.Name);
    }
}
