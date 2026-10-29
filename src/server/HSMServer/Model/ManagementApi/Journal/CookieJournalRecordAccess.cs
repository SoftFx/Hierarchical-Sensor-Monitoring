using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Model.Authentication;
using HSMServer.Model.ManagementApi.ChangeSets;
using System;

namespace HSMServer.Model.ManagementApi.Journal
{
    /// <summary>
    /// The cookie-`User` twin of the token evaluator's per-record sight
    /// checks (#1588 web UI): the evaluator resolves HsmApiToken principals
    /// only, so the Journal page maps the web user's role predicates onto
    /// the same resources. Admin sees everything; product/folder roles gate
    /// their own entities; global records (schedules, unresolvable legacy
    /// rows) stay admin-only.
    /// </summary>
    internal sealed class CookieJournalRecordAccess : IJournalRecordAccess
    {
        private readonly User _user;
        private readonly ITreeValuesCache _cache;

        public CookieJournalRecordAccess(User user, ITreeValuesCache cache)
        {
            _user = user;
            _cache = cache;
        }

        // The compensating writes of a web-side rollback authorize through
        // the engine's cookie seam (CookieUserAccess).
        public CookieUserAccess ChangeSetAccess => new(_user, _cache);

        HSMServer.Model.ManagementApi.ChangeSets.IChangeSetAccess
            IJournalRecordAccess.ChangeSetAccess => ChangeSetAccess;

        public bool IsAdmin => _user is { IsAdmin: true };

        public bool CanRead(ApiTokenResource resource) =>
            _user is null ? false : resource.Kind switch
            {
                ApiTokenResourceKind.Global => _user.IsAdmin,
                ApiTokenResourceKind.Folder => _user.IsAdmin || _user.IsFolderManager(resource.Id),
                ApiTokenResourceKind.Product => _user.IsProductAvailable(resource.Id),
                ApiTokenResourceKind.Sensor => _user.IsProductAvailable(RootProductOf(resource.Id)),
                _ => false,
            };

        private Guid RootProductOf(Guid sensorId) =>
            _cache.GetSensor(sensorId)?.Root?.Id ?? sensorId;
    }
}
