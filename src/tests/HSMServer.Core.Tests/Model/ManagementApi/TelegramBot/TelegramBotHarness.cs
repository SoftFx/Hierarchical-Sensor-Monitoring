using HSMServer.Authentication;
using HSMServer.Core.Journal;
using HSMServer.Core.Model;
using HSMServer.Model.Authentication;
using HSMServer.Model.ManagementApi.TelegramBot;
using HSMServer.Notifications;
using HSMServer.ServerConfiguration;
using Moq;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;

namespace HSMServer.Core.Tests.Model.ManagementApi.TelegramBot
{
    // The world of the bot-credential surface (#1830), shared by the service's, the REST
    // controller's and the MCP tools' tests: the stored settings behind the real
    // configuration object, the Telegram side behind the runtime seam, the evaluator's
    // decisions, the token's owner and the journal. The bot starts up, running on the
    // stored credential, which Telegram knows as the stored bot's username.
    internal sealed class TelegramBotHarness
    {
        public const string OldToken = "110201543:AAE-old-token-value-xxxxxxxxxxxxxxx";
        public const string NewToken = "220301543:AAE-new-token-value-yyyyyyyyyyyyyyy";

        public const string OldName = "hsm_alerts_bot";
        public const string NewName = "hsm_oncall_bot";

        public static readonly Guid OwnerId = Guid.NewGuid();

        private static readonly string TokenId = new('A', ApiTokenMaterial.TokenIdLength);

        public TelegramConfig Telegram { get; } = new() { BotName = OldName, BotToken = OldToken, IsRunning = true };

        public Mock<IServerConfig> Config { get; } = new();

        public Mock<ITelegramBotRuntime> Bot { get; } = new();

        public Mock<IApiTokenAuthorizationService> Authorization { get; } = new();

        public Mock<IUserManager> Users { get; } = new();

        public Mock<IJournalService> JournalService { get; } = new();

        public JournalAppender Journal { get; }

        // What a write did, in the order it did it.
        public List<string> Steps { get; } = [];

        public List<JournalRecordModel> Records { get; } = [];

        // Whether the bot is up — what the runtime answers and what a restart changes.
        public bool IsRunning { get; set; } = true;

        public User Owner { get; } = new("admin") { Id = OwnerId, IsAdmin = true };

        public ClaimsPrincipal Caller { get; }


        public TelegramBotHarness()
        {
            var identity = new ClaimsIdentity(
            [
                new Claim(HsmApiTokenClaims.OwnerUserId, OwnerId.ToString()),
                new Claim(HsmApiTokenClaims.TokenId, TokenId),
            ], HsmApiTokenDefaults.AuthenticationScheme);

            Caller = new ClaimsPrincipal(identity);

            Config.SetupGet(c => c.Telegram).Returns(Telegram);
            Config.Setup(c => c.ResaveSettings()).Callback(() => Steps.Add("save"));

            Bot.SetupGet(b => b.IsBotRunning).Returns(() => IsRunning);
            // A running bot remembers the username of the token it started with.
            Bot.Setup(b => b.UsernameOfRunningBot(It.IsAny<string>()))
                .Returns((string token) => IsRunning && token == OldToken ? OldName : null);
            TelegramAnswers(_ => OldName);
            RestartSucceeds();

            Users.Setup(u => u[OwnerId]).Returns(Owner);

            // An admin owner entitled by default; the refusal scenarios override one decision.
            Authorization.Setup(a => a.AuthorizeServerSettingsRead(It.IsAny<ClaimsPrincipal>()))
                .Returns(ApiTokenAuthorization.Allowed);
            Authorization.Setup(a => a.AuthorizeServerSettingsWrite(It.IsAny<ClaimsPrincipal>()))
                .Returns(ApiTokenAuthorization.Allowed);

            Journal = new JournalAppender(JournalService.Object, TimeProvider.System);
            JournalService.Setup(j => j.AddRecord(It.IsAny<JournalRecordModel>())).Callback<JournalRecordModel>(Records.Add);
        }


        public TelegramBotAdministrationService CreateService() =>
            new(Config.Object, Bot.Object, Authorization.Object, Users.Object, Journal);

        // The bot is up after the restart — the usual outcome of a start on a token Telegram
        // has just accepted.
        public void RestartSucceeds() => RestartAnswers(string.Empty);

        // The restart ends with the bot stopped and answers the bot's own error text.
        public void RestartFails(string error) => RestartAnswers(error);

        // The restart blows up before the bot is up: a start that throws leaves no bot running.
        public void RestartThrows(Exception failure) =>
            Bot.Setup(b => b.RestartAsync()).Returns(() =>
            {
                Steps.Add("restart");
                IsRunning = false;
                return Task.FromException<string>(failure);
            });

        private void RestartAnswers(string answer) =>
            Bot.Setup(b => b.RestartAsync()).Returns(() =>
            {
                Steps.Add("restart");
                IsRunning = answer == string.Empty;
                return Task.FromResult(answer);
            });

        // Telegram's answer to "who owns this token?" — every token unless a test narrows it.
        public void TelegramAnswers(Func<string, string> usernameOf) =>
            TelegramAnswers(token => TelegramTokenProbe.Accepted(usernameOf(token)));

        public void TelegramRefuses(Func<string, TelegramTokenProbe> answer) => TelegramAnswers(answer);

        private void TelegramAnswers(Func<string, TelegramTokenProbe> answer) =>
            Bot.Setup(b => b.GetBotUsernameAsync(It.IsAny<string>())).Returns<string>(token =>
            {
                Steps.Add("probe");
                return Task.FromResult(answer(token));
            });
    }
}
