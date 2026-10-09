using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using HSMCommon.TaskResult;
using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Schedule;
using HSMServer.Model.ManagementApi.Alerts;
using ScheduleModel = HSMServer.Core.Model.Policies.AlertSchedule;

namespace HSMServer.Model.ManagementApi.AlertSchedules
{
    /// <summary>
    /// The write engine of the schedule CRUD — the schedules' twin of
    /// <see cref="PolicyAdministrationService"/> (#1500 pattern): transport-
    /// agnostic, returning <see cref="PolicyWriteResult{T}"/> so the REST
    /// controller and the MCP write tools render the same decision.
    /// </summary>
    // Authorization follows the direction fixed when the read surface landed
    // (management-api feature.md): the write must NOT reuse the caller-wide
    // read gate (CanSeeAnyBoundary — the read-side equivalent of "the UI
    // shows schedules to every logged-in user"), because writes at the
    // GLOBAL boundary are admin-only in the evaluator. The web UI gates
    // SavePartial/Remove with [AuthorizeIsAdmin] (a cookie-principal check);
    // the token-side equivalent is AuthorizeWrite(User, GlobalScope):
    // admin owner + read-write token, with the evaluator's split preserved —
    // a non-admin owner answers 404 (Global sight is admin-only, so a
    // non-admin learns nothing about the surface), an admin owner with a
    // read-only token answers 403 (the method backstop rejects read-only
    // tokens on unsafe methods before the action anyway).
    //
    // Semantics ported from the web controller (AlertSchedulesController):
    //  - name uniqueness is GLOBAL (exact, case-sensitive, excluding self);
    //  - the parser throws on bad YAML and on semantically invalid schedules
    //    (windows >= 1 min, non-overlapping, start < end, dates 2000..2100)
    //    — the message becomes the field-keyed 422 detail;
    //  - timezone validation is NEW server-side (the web UI relies on its
    //    dropdown; the API validates system timezone ids — IANA and Windows
    //    both resolve — through TimeZoneInfo, the same lookup AlertSchedule
    //    uses at evaluation, so anything accepted resolves at runtime);
    //  - delete detaches FIRST: on any incomplete detach the schedule
    //    SURVIVES (409) so the retry re-runs the idempotent detach over the
    //    survivors — deleting it would strand the surviving references
    //    permanently (#1409). The web Remove's unconditional-detach variant
    //    (no existence guard, cleaning rows the loader skipped) stays a
    //    cookie-UI capability; the API answers 404 for an unknown id.
    public sealed class AlertScheduleAdministrationService
    {
        public const int MaxNameLength = 200;

        private readonly IAlertScheduleProvider _schedules;
        private readonly ITreeValuesCache _cache;
        private readonly IApiTokenAuthorizationService _authorization;
        private readonly AlertReadService _reader;

        // Stateless YAML machinery (the web controller holds one the same way);
        // not DI-registered anywhere today.
        private readonly AlertScheduleParser _parser = new();

        public AlertScheduleAdministrationService(IAlertScheduleProvider schedules, ITreeValuesCache cache,
            IApiTokenAuthorizationService authorization, AlertReadService reader)
        {
            _schedules = schedules;
            _cache = cache;
            _authorization = authorization;
            _reader = reader;
        }


        /// <summary>
        /// Create a schedule. The server generates the id (a client cannot
        /// choose one — the provider's Save is an upsert by id); the response
        /// echoes the stored schedule.
        /// </summary>
        public Task<PolicyWriteResult<AlertScheduleDto>> CreateScheduleAsync(ClaimsPrincipal user,
            AlertScheduleUpsertDto dto) =>
            Task.FromResult(Write(user, id: null, dto));

        /// <summary>
        /// Update a schedule (upsert semantics at a known id); the response
        /// echoes the stored schedule.
        /// </summary>
        public Task<PolicyWriteResult<AlertScheduleDto>> UpdateScheduleAsync(Guid id, ClaimsPrincipal user,
            AlertScheduleUpsertDto dto) =>
            Task.FromResult(Write(user, id, dto));

        /// <summary>
        /// Delete a schedule. Detaches the id from every referencing policy
        /// FIRST; on an incomplete detach the schedule survives and the answer
        /// is a Conflict (retry re-runs the idempotent detach).
        /// </summary>
        public async Task<PolicyWriteResult> DeleteScheduleAsync(Guid id, ClaimsPrincipal user,
            CancellationToken cancellationToken = default)
        {
            if (AuthorizeGlobalWrite(user) is { } denied)
                return PolicyWriteResult.Fail(denied.Outcome, message: denied.Message);

            if (_schedules.GetSchedule(id) is null)
                return PolicyWriteResult.Fail(PolicyWriteOutcome.NotFound);

            // Deliberately tied to the cancellation token like the web Remove:
            // an aborted request stops the DISPATCH of further detach work;
            // the schedule is not deleted either way (see the class comment).
            var detach = await _cache.DetachAlertScheduleFromPoliciesAsync(id, cancellationToken);

            if (!detach.IsOk)
                return PolicyWriteResult.Fail(PolicyWriteOutcome.Conflict,
                    message:
                    "The schedule was not deleted — not every policy reference could be cleared. Delete it again to retry. " +
                    detach.Error);

            _schedules.DeleteSchedule(id);

            return PolicyWriteResult.Ok();
        }


        private PolicyWriteResult<AlertScheduleDto> Write(ClaimsPrincipal user, Guid? id,
            AlertScheduleUpsertDto dto)
        {
            if (AuthorizeGlobalWrite(user) is { } denied)
                return PolicyWriteResult<AlertScheduleDto>.Fail(denied.Outcome, message: denied.Message);

            // Authorization precedes body validation and existence (#1500
            // rule): a body-shape error must not reveal anything to a caller
            // outside the surface's reach. On update, an unknown id is a
            // plain 404 for an entitled caller.
            if (id is not null && _schedules.GetSchedule(id.Value) is null)
                return PolicyWriteResult<AlertScheduleDto>.Fail(PolicyWriteOutcome.NotFound);

            // A JSON-null body reaches here only from MCP (REST's
            // [ApiController] binding rejects it with a 400 before the
            // service runs): answer the field-keyed shape the tool contract
            // promises, never a NullReferenceException.
            if (dto is null)
                return PolicyWriteResult<AlertScheduleDto>.Fail(PolicyWriteOutcome.Invalid,
                    new Dictionary<string, string[]> { ["schedule"] = ["The schedule body is required."] });

            if (!TryBuildValidatedSchedule(dto, id, out var schedule, out var errors))
                return PolicyWriteResult<AlertScheduleDto>.Fail(PolicyWriteOutcome.Invalid, errors);

            // The provider's Save is an idempotent upsert (cache + LevelDB row).
            _schedules.SaveSchedule(schedule);

            // Echo the STORED schedule through the read mapping — sensor
            // references filtered to the owner's sight, exactly as GET does.
            return PolicyWriteResult<AlertScheduleDto>.Ok(_reader.RenderSchedule(schedule, user));
        }


        // Field validation accumulates across name/timezone/schedule — the
        // templates' structural pass shape — and the YAML parse runs inside a
        // try (the domain parser throws on legal-looking but invalid input)
        // whatever the other fields say, so a caller with several problems
        // learns them all in one 422.
        private bool TryBuildValidatedSchedule(AlertScheduleUpsertDto dto, Guid? id,
            out ScheduleModel schedule, out IDictionary<string, string[]> errors)
        {
            var errorMap = new Dictionary<string, List<string>>();

            if (string.IsNullOrWhiteSpace(dto.Name))
                errorMap.Add("name", ["The schedule name is required."]);
            else if (dto.Name.Length > MaxNameLength)
                errorMap.Add("name", [$"The schedule name must be at most {MaxNameLength} characters."]);
            else if ((_schedules.GetAllSchedules() ?? []).Any(
                     existing => existing.Name == dto.Name && existing.Id != id))
                errorMap.Add("name", ["The schedule name must be unique."]);

            if (string.IsNullOrWhiteSpace(dto.Timezone))
            {
                errorMap.Add("timezone", ["The timezone is required."]);
            }
            else
            {
                try
                {
                    TimeZoneInfo.FindSystemTimeZoneById(dto.Timezone);
                }
                catch (Exception)
                {
                    errorMap.Add("timezone",
                        [$"The timezone '{dto.Timezone}' is not a known system timezone id (IANA or Windows)."]);
                }
            }

            schedule = null;

            try
            {
                schedule = _parser.Parse(dto.Schedule ?? string.Empty);

                // The id is server-owned: generated on create, the route id on
                // update — never a client-chosen value. Assigned only on the
                // success path below.
                schedule.Id = id ?? Guid.NewGuid();
                schedule.Name = dto.Name;
                schedule.Timezone = dto.Timezone;
            }
            catch (Exception ex)
            {
                errorMap.Add("schedule", [ex.Message]);
            }

            errors = null;

            if (errorMap.Count == 0)
                return true;

            errors = errorMap.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
            return false;
        }


        // The evaluator's decision at the GLOBAL boundary: admin owner +
        // read-write token. The 403/404 split is preserved verbatim — a
        // non-admin owner's Global sight is false, so the answer is the same
        // 404 as an unknown id (nothing about the surface's existence leaks).
        private PolicyWriteFailure AuthorizeGlobalWrite(ClaimsPrincipal user) =>
            _authorization.AuthorizeWrite(user, ApiTokenResource.GlobalScope) switch
            {
                ApiTokenAuthorization.Allowed => null,
                ApiTokenAuthorization.Forbidden => new PolicyWriteFailure
                {
                    Outcome = PolicyWriteOutcome.Forbidden,
                    Message = PolicyWriteResult.WriteDeniedMessage,
                },
                _ => new PolicyWriteFailure { Outcome = PolicyWriteOutcome.NotFound },
            };
    }
}
