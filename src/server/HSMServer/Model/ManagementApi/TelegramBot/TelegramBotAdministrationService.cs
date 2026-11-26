using HSMServer.Authentication;
using HSMServer.Core.Journal;
using HSMServer.Model.Authentication;
using HSMServer.Model.Configuration;
using HSMServer.Model.ManagementApi;
using HSMServer.Model.ManagementApi.Alerts;
using HSMServer.Model.ManagementApi.ChangeSets;
using HSMServer.Notifications;
using HSMServer.ServerConfiguration;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;

namespace HSMServer.Model.ManagementApi.TelegramBot
{
    /// <summary>
    /// The bot-credential surface of the management API (#1830) — the first server-settings
    /// surface of the area: the status of the Telegram bot, and the update that saves and
    /// applies a change in one call. Transport-agnostic: the REST controller and the MCP
    /// tools both call it and get the area's envelopes back, never an IActionResult.
    /// </summary>
    public sealed class TelegramBotAdministrationService
    {
        // The read's 403: the settings have no sight rule of their own beyond the admin role
        // (the web Configuration page's [AuthorizeIsAdmin]). Writes answer the area's one
        // 403 message.
        private const string NotAdminMessage = "The token's owner is not an administrator.";

        private const string TokenRejectedMessage = "Telegram rejected the bot token: {0}.";

        private const string CurrentTokenRejectedMessage =
            "Telegram rejected the current bot token ({0}); the bot name could not be checked. Nothing was changed.";

        private const string NoTokenForTheNameMessage =
            "The bot name can only be checked against a bot token, and none is stored; pass botToken.";

        // Fixed text, on purpose: what went wrong on the road to Telegram is the server's to
        // log, not the caller's to read — and the same call may simply be made again.
        private const string TokenUnreachableMessage =
            "Telegram could not be reached to check the bot token; nothing was changed. Try again later.";

        private const string NameUnreachableMessage =
            "Telegram could not be reached to check the bot name; nothing was changed. Try again later.";

        private const string NameMismatchMessage =
            "The bot name does not match the bot of the {0} token; Telegram reports '{1}'.";

        private const string EmptyTokenMessage = "The bot token must not be empty; omit it to keep the stored one.";

        private const string EmptyNameMessage = "The bot name must not be empty; omit it to keep the stored one.";

        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(nameof(TelegramBotAdministrationService));

        private readonly IServerConfig _config;
        private readonly ITelegramBotRuntime _bot;
        private readonly IApiTokenAuthorizationService _authorization;
        private readonly IUserManager _users;
        private readonly JournalAppender _journal;


        public TelegramBotAdministrationService(IServerConfig config, ITelegramBotRuntime bot,
            IApiTokenAuthorizationService authorization, IUserManager users, JournalAppender journal)
        {
            _config = config;
            _bot = bot;
            _authorization = authorization;
            _users = users;
            _journal = journal;
        }


        /// <summary>
        /// The status of the bot credential: the name, the switch, whether a token is stored
        /// (never the token) and whether the bot is up. Administrators only.
        /// </summary>
        public ManagementReadResult<TelegramBotDto> GetStatus(ClaimsPrincipal caller)
        {
            if (_authorization.AuthorizeServerSettingsRead(caller) != ApiTokenAuthorization.Allowed)
                return ManagementReadResult<TelegramBotDto>.Fail(ManagementReadOutcome.Forbidden, message: NotAdminMessage);

            return ManagementReadResult<TelegramBotDto>.Ok(Status());
        }

        /// <summary>
        /// Change the bot credential: a partial update, administrators only. A new token is
        /// asked of Telegram BEFORE anything is saved; the save is journaled; only a token
        /// change restarts the bot. The echo is the resulting status.
        /// </summary>
        public async Task<PolicyWriteResult<TelegramBotDto>> UpdateAsync(UpdateTelegramBotRequestDto request, ClaimsPrincipal caller)
        {
            // Admission first (the area's rule: it precedes body validation).
            if (_authorization.AuthorizeServerSettingsWrite(caller) != ApiTokenAuthorization.Allowed)
                return PolicyWriteResult<TelegramBotDto>.Fail(PolicyWriteOutcome.Forbidden, message: PolicyWriteResult.WriteDeniedMessage);

            request ??= new();

            if (ShapeProblems(request) is { } problems)
                return PolicyWriteResult<TelegramBotDto>.Fail(PolicyWriteOutcome.Invalid, problems);

            // One snapshot: the settings are read here and applied against the same values below.
            var current = TelegramBotSettings.Of(_config.Telegram);
            var target = new TelegramBotSettings(
                BotName: request.BotName?.Trim() ?? current.BotName,
                BotToken: request.BotToken?.Trim() ?? current.BotToken,
                IsEnabled: request.IsEnabled ?? current.IsEnabled);
            var tokenChanges = JournalAppender.Change.SecretChanged(current.BotToken, target.BotToken);
            var changes = TelegramSettingsChanges.Between(current, target);
            var nameGiven = request.BotName is not null;

            // An update that changes nothing does nothing: no probe, no save, no restart. A
            // name that was passed is still checked — a caller repeating a stored typo is
            // told it is one.
            if (changes.Count == 0 && !nameGiven)
                return PolicyWriteResult<TelegramBotDto>.Ok(Status());

            // 1. A new token is asked of Telegram BEFORE anything is saved: a rejected token
            //    changes nothing, and the working bot keeps running.
            string username = null;

            if (tokenChanges)
            {
                var probe = await _bot.GetBotUsernameAsync(target.BotToken);

                if (probe.Verdict == TelegramTokenVerdict.Rejected)
                    return Refused("botToken", string.Format(TokenRejectedMessage, probe.Reason));

                if (probe.Verdict == TelegramTokenVerdict.Unreachable)
                    return PolicyWriteResult<TelegramBotDto>.Fail(PolicyWriteOutcome.Conflict, message: TokenUnreachableMessage);

                username = probe.Username;
            }

            // 2. The name goes with the token — invitation links are built from it. A name
            //    that was passed must be the bot of the effective token (the new one when
            //    there is one, otherwise the stored one); after a token change the stored
            //    name must be too, for an omitted field does not change.
            if (nameGiven || tokenChanges)
            {
                var effectiveToken = tokenChanges ? target.BotToken : current.BotToken;

                // The running bot was told its username when it started on this very token:
                // then Telegram need not be asked.
                username ??= _bot.UsernameOfRunningBot(effectiveToken);

                if (username is null)
                {
                    if (string.IsNullOrEmpty(effectiveToken))
                        return Refused("botToken", NoTokenForTheNameMessage);

                    var probe = await _bot.GetBotUsernameAsync(effectiveToken);

                    if (probe.Verdict == TelegramTokenVerdict.Rejected)
                        return Refused("botToken", string.Format(CurrentTokenRejectedMessage, probe.Reason));

                    if (probe.Verdict == TelegramTokenVerdict.Unreachable)
                        return PolicyWriteResult<TelegramBotDto>.Fail(PolicyWriteOutcome.Conflict, message: NameUnreachableMessage);

                    username = probe.Username;
                }

                if (!IsTheNameOf(target.BotName, username))
                    return Refused("botName", string.Format(NameMismatchMessage, tokenChanges ? "new" : "current", username));
            }

            if (changes.Count == 0)
                return PolicyWriteResult<TelegramBotDto>.Ok(Status());

            TelegramSettingsChanges.Apply(_config, current, target);

            var initiator = ChangeSetRecorder.InitiatorOf(caller, _users);
            TelegramSettingsChanges.Write(changes, _journal, initiator);
            _logger.Info(TelegramSettingsChanges.Describe(changes, initiator.Info));

            // Only a token change restarts the bot: the name and the switch are read live.
            if (tokenChanges)
            {
                // The save has landed. A start that fails — or throws — must not turn this
                // into a failure the caller would read as "not saved" (the same call again
                // would then change nothing and restart nothing): the echo says the bot is
                // not running, the log says why.
                try
                {
                    if (await _bot.RestartAsync() is { Length: > 0 } error)
                        _logger.Warn($"The Telegram bot credential was saved, but the bot did not start: {error}");
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "The Telegram bot credential was saved, but restarting the bot threw; the bot is not running.");
                }
            }

            return PolicyWriteResult<TelegramBotDto>.Ok(Status());
        }

        // Telegram usernames are case-insensitive; an absent username matches no name.
        private static bool IsTheNameOf(string name, string username) =>
            username is not null && string.Equals(name?.Trim(), username, StringComparison.OrdinalIgnoreCase);

        // An empty value means empty, not "clear": clearing the credential stays a web page
        // action. Both empties answer together, like every problem of one request in the area.
        private static Dictionary<string, string[]> ShapeProblems(UpdateTelegramBotRequestDto request)
        {
            var problems = new Dictionary<string, string[]>();

            if (request.BotToken is not null && string.IsNullOrWhiteSpace(request.BotToken))
                problems["botToken"] = [EmptyTokenMessage];

            if (request.BotName is not null && string.IsNullOrWhiteSpace(request.BotName))
                problems["botName"] = [EmptyNameMessage];

            return problems.Count == 0 ? null : problems;
        }

        private static PolicyWriteResult<TelegramBotDto> Refused(string field, string message) =>
            PolicyWriteResult<TelegramBotDto>.Fail(PolicyWriteOutcome.Invalid,
                new Dictionary<string, string[]> { [field] = [message] });

        // The status read back from the stored settings and the bot — never the token.
        private TelegramBotDto Status() =>
            new()
            {
                BotName = _config.Telegram.BotName,
                IsEnabled = _config.Telegram.IsRunning,
                HasBotToken = !string.IsNullOrEmpty(_config.Telegram.BotToken),
                IsRunning = _bot.IsBotRunning,
            };
    }


    /// <summary>
    /// The Telegram side the bot-credential surface needs from the running bot (#1830):
    /// whether it is up, the username it remembers from its start, the answer to "who owns
    /// this token?" and the restart. The bot behind NotificationsCenter is a sealed concrete
    /// type, so the seam is this adapter (the IChatDelivery pattern).
    /// </summary>
    public interface ITelegramBotRuntime
    {
        bool IsBotRunning { get; }

        /// <summary>
        /// The username Telegram reported when the running bot started, when it runs on
        /// exactly this token; null otherwise — the caller asks Telegram itself.
        /// </summary>
        string UsernameOfRunningBot(string token);

        /// <summary>
        /// Asks Telegram who owns a token. Nothing of the running bot is touched; the
        /// outcome is answered, never thrown.
        /// </summary>
        Task<TelegramTokenProbe> GetBotUsernameAsync(string token);

        /// <summary>
        /// Stops the running bot, if any, and starts it on the stored credential. Answers an
        /// empty string when the bot is running afterwards, the bot's own error text (the
        /// token wiped) when it is not.
        /// </summary>
        Task<string> RestartAsync();
    }

    public sealed class NotificationsCenterTelegramBotRuntime : ITelegramBotRuntime
    {
        private readonly NotificationsCenter _notifications;

        public NotificationsCenterTelegramBotRuntime(NotificationsCenter notifications) =>
            _notifications = notifications;

        public bool IsBotRunning => _notifications.TelegramBot.IsBotRunning;

        public string UsernameOfRunningBot(string token) => _notifications.TelegramBot.UsernameOfRunningBot(token);

        public Task<TelegramTokenProbe> GetBotUsernameAsync(string token) =>
            _notifications.TelegramBot.GetBotUsernameAsync(token);

        public Task<string> RestartAsync() => _notifications.TelegramBot.StartBotAsync();
    }
}
