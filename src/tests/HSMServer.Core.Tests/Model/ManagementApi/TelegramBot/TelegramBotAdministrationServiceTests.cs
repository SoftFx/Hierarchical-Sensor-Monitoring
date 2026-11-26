using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using HSMServer.Authentication;
using HSMServer.Core.Journal;
using HSMServer.Core.Model;
using HSMServer.Model.ManagementApi;
using HSMServer.Model.ManagementApi.Alerts;
using HSMServer.Model.ManagementApi.TelegramBot;
using HSMServer.Notifications;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.Model.ManagementApi.TelegramBot
{
    // #1830: the bot-credential surface's engine — the status, and the update whose order is
    // the point: a new token is asked of Telegram BEFORE anything is saved, then saved and
    // journaled, then — and only then — the bot is restarted. The Telegram side (the probe,
    // the running bot's username, the restart) is the runtime seam; the evaluator's decisions
    // are inputs here (its own matrix lives in ApiTokenAuthorizationServiceTests).
    public sealed class TelegramBotAdministrationServiceTests
    {
        private readonly TelegramBotHarness _world = new();

        private TelegramBotAdministrationService Service => _world.CreateService();

        private List<(string Property, string Old, string New)> Records =>
            _world.Records.Select(r => (r.PropertyName, r.OldValue, r.NewValue)).ToList();


        // ---- the status ---------------------------------------------------------------


        [Fact]
        public void GetStatus_AnswersTheFourFields_NeverTheToken()
        {
            var status = Service.GetStatus(_world.Caller).Value;

            Assert.Equal(new TelegramBotDto { BotName = "hsm_alerts_bot", IsEnabled = true, HasBotToken = true, IsRunning = true }, status);
            Assert.DoesNotContain(TelegramBotHarness.OldToken, JsonSerializer.Serialize(status));
        }


        [Fact]
        public void GetStatus_NonAdmin_IsForbidden_AndTheSettingsAreNotRead()
        {
            _world.Authorization.Setup(a => a.AuthorizeServerSettingsRead(It.IsAny<System.Security.Claims.ClaimsPrincipal>()))
                .Returns(ApiTokenAuthorization.Forbidden);

            var result = Service.GetStatus(_world.Caller);

            Assert.Equal(ManagementReadOutcome.Forbidden, result.Failure.Outcome);
            Assert.Equal("The token's owner is not an administrator.", result.Failure.Message);
            _world.Config.VerifyGet(c => c.Telegram, Times.Never);
        }


        // ---- a new token ----------------------------------------------------------------


        [Fact]
        public async Task Update_AValidTokenWithItsName_IsProbedThenSavedThenRestarted_AndTheEchoSaysTheBotRuns()
        {
            _world.TelegramAnswers(_ => "hsm_oncall_bot");

            var result = await Service.UpdateAsync(
                new UpdateTelegramBotRequestDto { BotName = "hsm_oncall_bot", BotToken = TelegramBotHarness.NewToken }, _world.Caller);

            Assert.Equal(new TelegramBotDto { BotName = "hsm_oncall_bot", IsEnabled = true, HasBotToken = true, IsRunning = true }, result.Value);
            Assert.Equal(TelegramBotHarness.NewToken, _world.Telegram.BotToken);
            Assert.Equal("hsm_oncall_bot", _world.Telegram.BotName);
            Assert.Equal(["probe", "save", "restart"], _world.Steps);
            _world.Bot.Verify(b => b.GetBotUsernameAsync(TelegramBotHarness.NewToken), Times.Once);
        }


        [Fact]
        public async Task Update_NonAdmin_IsForbidden_NothingIsProbedSavedOrRestarted()
        {
            _world.Authorization.Setup(a => a.AuthorizeServerSettingsWrite(It.IsAny<System.Security.Claims.ClaimsPrincipal>()))
                .Returns(ApiTokenAuthorization.Forbidden);

            var result = await Service.UpdateAsync(new UpdateTelegramBotRequestDto { BotToken = TelegramBotHarness.NewToken }, _world.Caller);

            Assert.Equal(PolicyWriteOutcome.Forbidden, result.Failure.Outcome);
            Assert.Equal(PolicyWriteResult.WriteDeniedMessage, result.Failure.Message);
            Assert.Empty(_world.Steps);
            Assert.Equal(TelegramBotHarness.OldToken, _world.Telegram.BotToken);
            Assert.Empty(_world.Records);
        }


        [Fact]
        public async Task Update_EveryChange_LeavesAServerSettingsRecord_NamingTheTokensOwner_NeverTheToken()
        {
            _world.TelegramAnswers(_ => "hsm_oncall_bot");

            await Service.UpdateAsync(
                new UpdateTelegramBotRequestDto { BotName = "hsm_oncall_bot", BotToken = TelegramBotHarness.NewToken, IsEnabled = false }, _world.Caller);

            Assert.Equal(
                [("Enable messages", "True", "False"), ("Bot token", "set", "changed"), ("Bot name", "hsm_alerts_bot", "hsm_oncall_bot")],
                Records);
            Assert.Equal(3, _world.Records.Count);

            Assert.All(_world.Records, record =>
            {
                Assert.Equal(JournalKinds.ServerSettings, record.Kind);
                Assert.Equal("User (admin)", record.Initiator);

                // Every text cell a record can carry.
                var text = string.Join("\n", record.Enviroment, record.Initiator, record.PropertyName, record.OldValue, record.NewValue, record.Path);
                Assert.DoesNotContain(TelegramBotHarness.OldToken, text);
                Assert.DoesNotContain(TelegramBotHarness.NewToken, text);
            });
        }


        // ---- a token Telegram does not confirm ------------------------------------------


        [Fact]
        public async Task Update_ATokenTelegramRejects_IsRefusedUnderBotToken_AndNothingChanges()
        {
            // The working bot keeps running: a mistyped token must not take delivery down
            // the way the web page's Restart does.
            _world.TelegramRefuses(_ => TelegramTokenProbe.Rejected("Unauthorized"));

            var result = await Service.UpdateAsync(new UpdateTelegramBotRequestDto { BotToken = TelegramBotHarness.NewToken }, _world.Caller);

            Assert.Equal(PolicyWriteOutcome.Invalid, result.Failure.Outcome);
            Assert.Equal(["Telegram rejected the bot token: Unauthorized."], result.Failure.Errors["botToken"]);
            Assert.Equal(["probe"], _world.Steps);
            Assert.Equal(TelegramBotHarness.OldToken, _world.Telegram.BotToken);
            Assert.Empty(_world.Records);
            Assert.True(_world.IsRunning);
        }


        [Fact]
        public async Task Update_TelegramIsNotReached_IsTheFixedConflict_AndNothingChanges()
        {
            // No road to Telegram says nothing about the token: the fixed message carries
            // none of the transport's own text, and the same call may be made again.
            _world.TelegramRefuses(_ => TelegramTokenProbe.Unreachable("GET https://api.telegram.org/ timed out"));

            var result = await Service.UpdateAsync(new UpdateTelegramBotRequestDto { BotToken = TelegramBotHarness.NewToken }, _world.Caller);

            Assert.Equal(PolicyWriteOutcome.Conflict, result.Failure.Outcome);
            Assert.Equal("Telegram could not be reached to check the bot token; nothing was changed. Try again later.", result.Failure.Message);
            Assert.Equal(["probe"], _world.Steps);
            Assert.Equal(TelegramBotHarness.OldToken, _world.Telegram.BotToken);
            Assert.Empty(_world.Records);
        }


        // ---- the name that goes with the token ------------------------------------------


        [Fact]
        public async Task Update_ANameOfAnotherBot_IsRefusedUnderBotName_AndNothingIsSaved()
        {
            // Invitation links are built from the name: a typo would break them silently.
            _world.TelegramAnswers(_ => "hsm_oncall_bot");

            var result = await Service.UpdateAsync(
                new UpdateTelegramBotRequestDto { BotName = "typo_bot", BotToken = TelegramBotHarness.NewToken }, _world.Caller);

            Assert.Equal(PolicyWriteOutcome.Invalid, result.Failure.Outcome);
            Assert.Equal(["The bot name does not match the bot of the new token; Telegram reports 'hsm_oncall_bot'."],
                result.Failure.Errors["botName"]);
            Assert.Equal(["probe"], _world.Steps);
            Assert.Equal(TelegramBotHarness.OldToken, _world.Telegram.BotToken);
        }


        [Fact]
        public async Task Update_ATokenOfAnotherBot_UnderTheStoredName_IsRefusedUnderBotName_AndNothingIsSaved()
        {
            // An omitted field does not change — and a stored name of another bot must not
            // survive a token change either. The answer names the bot Telegram reported.
            _world.TelegramAnswers(_ => "other_bot");

            var result = await Service.UpdateAsync(new UpdateTelegramBotRequestDto { BotToken = TelegramBotHarness.NewToken }, _world.Caller);

            Assert.Equal(PolicyWriteOutcome.Invalid, result.Failure.Outcome);
            Assert.Equal(["The bot name does not match the bot of the new token; Telegram reports 'other_bot'."],
                result.Failure.Errors["botName"]);
            Assert.Equal(["probe"], _world.Steps);
            Assert.Equal(TelegramBotHarness.OldToken, _world.Telegram.BotToken);
        }


        [Fact]
        public async Task Update_ARegeneratedTokenOfTheSameBot_NeedsNoName()
        {
            _world.TelegramAnswers(_ => "hsm_alerts_bot");

            var result = await Service.UpdateAsync(new UpdateTelegramBotRequestDto { BotToken = TelegramBotHarness.NewToken }, _world.Caller);

            Assert.True(result.Success);
            Assert.Equal(TelegramBotHarness.NewToken, _world.Telegram.BotToken);
            Assert.Equal("hsm_alerts_bot", _world.Telegram.BotName);
            Assert.Equal(["probe", "save", "restart"], _world.Steps);
        }


        // ---- a name without a new token ---------------------------------------------------


        [Fact]
        public async Task Update_OnlyTheName_BotRunningOnTheStoredToken_NeedsNoProbe_AndRestartsNothing()
        {
            // The running bot was told its username when it started: asking Telegram again
            // would be one call too many.
            var result = await Service.UpdateAsync(new UpdateTelegramBotRequestDto { BotName = "  HSM_Alerts_Bot " }, _world.Caller);

            Assert.True(result.Success);
            Assert.Equal("HSM_Alerts_Bot", result.Value.BotName);
            Assert.Equal(["save"], _world.Steps);
            Assert.Equal([("Bot name", "hsm_alerts_bot", "HSM_Alerts_Bot")], Records);
        }


        [Fact]
        public async Task Update_OnlyTheName_OfAnotherBot_IsRefusedUnderBotName_AndNothingIsSaved()
        {
            var result = await Service.UpdateAsync(new UpdateTelegramBotRequestDto { BotName = "typo_bot" }, _world.Caller);

            Assert.Equal(PolicyWriteOutcome.Invalid, result.Failure.Outcome);
            Assert.Equal(["The bot name does not match the bot of the current token; Telegram reports 'hsm_alerts_bot'."],
                result.Failure.Errors["botName"]);

            // The running bot's memory does not vouch for the name: Telegram itself is asked.
            Assert.Equal(["probe"], _world.Steps);
            Assert.Equal("hsm_alerts_bot", _world.Telegram.BotName);
        }


        [Fact]
        public async Task Update_OnlyTheName_TheStoredNamePassedBack_IsCheckedToo_AndATypoIsCaught()
        {
            // The web form never checked the name. A name that was wrong all along is
            // refused when a caller passes it back — the retry of an earlier call included.
            _world.Telegram.BotName = "typo_bot";

            var result = await Service.UpdateAsync(new UpdateTelegramBotRequestDto { BotName = "typo_bot" }, _world.Caller);

            Assert.Equal(PolicyWriteOutcome.Invalid, result.Failure.Outcome);
            Assert.Equal(["The bot name does not match the bot of the current token; Telegram reports 'hsm_alerts_bot'."],
                result.Failure.Errors["botName"]);
            Assert.Equal(["probe"], _world.Steps);
        }


        [Fact]
        public async Task Update_OnlyTheName_TheBotWasRenamedInBotFather_IsAskedOfTelegram_AndTheNewNamePasses()
        {
            // BotFather can rename a bot. The username a running bot remembers from its
            // start is then stale: a name that does not match it is asked of Telegram itself,
            // and Telegram's answer is the one that counts — both ways.
            _world.TelegramAnswers(_ => "hsm_renamed_bot");

            var accepted = await Service.UpdateAsync(new UpdateTelegramBotRequestDto { BotName = "hsm_renamed_bot" }, _world.Caller);

            Assert.True(accepted.Success);
            Assert.Equal("hsm_renamed_bot", _world.Telegram.BotName);
            _world.Bot.Verify(b => b.GetBotUsernameAsync(TelegramBotHarness.OldToken), Times.Once);
            Assert.Equal(["probe", "save"], _world.Steps);

            var refused = await Service.UpdateAsync(new UpdateTelegramBotRequestDto { BotName = "typo_bot" }, _world.Caller);

            Assert.Equal(["The bot name does not match the bot of the current token; Telegram reports 'hsm_renamed_bot'."],
                refused.Failure.Errors["botName"]);
        }


        [Fact]
        public async Task Update_OnlyTheName_BotStopped_AsksTelegramOnce_WithTheStoredToken()
        {
            // A stopped bot remembers nothing: the stored token has to be asked about.
            _world.IsRunning = false;
            _world.TelegramAnswers(token => token == TelegramBotHarness.OldToken ? "hsm_alerts_bot" : "other_bot");

            var result = await Service.UpdateAsync(new UpdateTelegramBotRequestDto { BotName = "HSM_ALERTS_BOT" }, _world.Caller);

            Assert.True(result.Success);
            _world.Bot.Verify(b => b.GetBotUsernameAsync(TelegramBotHarness.OldToken), Times.Once);
            Assert.Equal(["probe", "save"], _world.Steps);
        }


        [Fact]
        public async Task Update_OnlyTheName_TheSavedTokenMovedOnFromTheRunningOne_AsksAboutTheSavedToken()
        {
            // The web page saves and restarts in two steps: the saved token may be a new
            // bot's while the running bot is still the old one. The username in memory is
            // the running bot's — not the saved token's.
            _world.Telegram.BotToken = TelegramBotHarness.NewToken;
            _world.TelegramAnswers(token => token == TelegramBotHarness.NewToken ? "hsm_oncall_bot" : "hsm_alerts_bot");

            var result = await Service.UpdateAsync(new UpdateTelegramBotRequestDto { BotName = "hsm_oncall_bot" }, _world.Caller);

            Assert.True(result.Success);
            _world.Bot.Verify(b => b.GetBotUsernameAsync(TelegramBotHarness.NewToken), Times.Once);
            _world.Bot.Verify(b => b.GetBotUsernameAsync(TelegramBotHarness.OldToken), Times.Never);
            Assert.Equal(["probe", "save"], _world.Steps);
        }


        [Fact]
        public async Task Update_OnlyTheName_NoTokenStored_IsRefusedUnderBotToken_AndNothingIsSaved()
        {
            _world.Telegram.BotName = null;
            _world.Telegram.BotToken = null;

            var result = await Service.UpdateAsync(new UpdateTelegramBotRequestDto { BotName = "hsm_alerts_bot" }, _world.Caller);

            Assert.Equal(PolicyWriteOutcome.Invalid, result.Failure.Outcome);
            Assert.Equal(["The bot name can only be checked against a bot token, and none is stored; pass botToken."],
                result.Failure.Errors["botToken"]);
            Assert.Empty(_world.Steps);
            Assert.Null(_world.Telegram.BotName);
        }


        [Fact]
        public async Task Update_OnlyTheName_TheStoredTokenRejectedByTelegram_IsRefusedUnderBotToken_AndNothingIsSaved()
        {
            // The name is not what is wrong — the stored token is, so the answer names that.
            _world.IsRunning = false;
            _world.TelegramRefuses(_ => TelegramTokenProbe.Rejected("Unauthorized"));

            var result = await Service.UpdateAsync(new UpdateTelegramBotRequestDto { BotName = "HSM_ALERTS_BOT" }, _world.Caller);

            Assert.Equal(PolicyWriteOutcome.Invalid, result.Failure.Outcome);
            Assert.Equal(["Telegram rejected the current bot token (Unauthorized); the bot name could not be checked. Nothing was changed."],
                result.Failure.Errors["botToken"]);
            Assert.Equal(["probe"], _world.Steps);
            Assert.Equal("hsm_alerts_bot", _world.Telegram.BotName);
        }


        [Fact]
        public async Task Update_OnlyTheName_TelegramIsNotReached_IsTheFixedConflict_AndNothingIsSaved()
        {
            _world.IsRunning = false;
            _world.TelegramRefuses(_ => TelegramTokenProbe.Unreachable("GET https://api.telegram.org/ timed out"));

            var result = await Service.UpdateAsync(new UpdateTelegramBotRequestDto { BotName = "HSM_ALERTS_BOT" }, _world.Caller);

            Assert.Equal(PolicyWriteOutcome.Conflict, result.Failure.Outcome);
            Assert.Equal("Telegram could not be reached to check the bot name; nothing was changed. Try again later.", result.Failure.Message);
            Assert.Equal(["probe"], _world.Steps);
            Assert.Equal("hsm_alerts_bot", _world.Telegram.BotName);
        }


        // ---- what needs no probe and no restart ------------------------------------------


        [Fact]
        public async Task Update_OnlyDisabled_NeedsNoProbe_AndRestartsNothing()
        {
            var result = await Service.UpdateAsync(new UpdateTelegramBotRequestDto { IsEnabled = false }, _world.Caller);

            Assert.Equal(new TelegramBotDto { BotName = "hsm_alerts_bot", IsEnabled = false, HasBotToken = true, IsRunning = true }, result.Value);
            Assert.Equal(["save"], _world.Steps);
            Assert.Equal([("Enable messages", "True", "False")], Records);
        }


        [Fact]
        public async Task Update_EnablingWithTheBotStopped_SavesTheSwitch_AndStartsNothing()
        {
            // The switch is read live; nothing but a token change starts the bot.
            _world.IsRunning = false;
            _world.Telegram.IsRunning = false;

            var result = await Service.UpdateAsync(new UpdateTelegramBotRequestDto { IsEnabled = true }, _world.Caller);

            Assert.Equal(new TelegramBotDto { BotName = "hsm_alerts_bot", IsEnabled = true, HasBotToken = true, IsRunning = false }, result.Value);
            Assert.Equal(["save"], _world.Steps);
        }


        [Fact]
        public async Task Update_NothingChanges_SavesProbesAndRestartsNothing()
        {
            var result = await Service.UpdateAsync(new UpdateTelegramBotRequestDto { IsEnabled = true }, _world.Caller);

            Assert.Equal(new TelegramBotDto { BotName = "hsm_alerts_bot", IsEnabled = true, HasBotToken = true, IsRunning = true }, result.Value);
            Assert.Empty(_world.Steps);
            Assert.Empty(_world.Records);
        }


        [Fact]
        public async Task Update_AnEmptyRequest_ChangesNothing()
        {
            var result = await Service.UpdateAsync(new UpdateTelegramBotRequestDto(), _world.Caller);

            Assert.True(result.Success);
            _world.Config.Verify(c => c.ResaveSettings(), Times.Never);
            _world.Bot.Verify(b => b.GetBotUsernameAsync(It.IsAny<string>()), Times.Never);
            _world.Bot.Verify(b => b.RestartAsync(), Times.Never);
        }


        // ---- the shape of the request -------------------------------------------------------


        [Fact]
        public async Task Update_AnEmptyNameAndToken_AreRefusedTogether_AndNothingChanges()
        {
            // Empty means empty, not "clear": clearing the credential stays a web page action.
            var result = await Service.UpdateAsync(new UpdateTelegramBotRequestDto { BotName = "  ", BotToken = " " }, _world.Caller);

            Assert.Equal(PolicyWriteOutcome.Invalid, result.Failure.Outcome);
            Assert.Equal(
                new Dictionary<string, string[]>
                {
                    ["botName"] = ["The bot name must not be empty; omit it to keep the stored one."],
                    ["botToken"] = ["The bot token must not be empty; omit it to keep the stored one."],
                },
                result.Failure.Errors);
            Assert.Empty(_world.Steps);
            Assert.Equal(TelegramBotHarness.OldToken, _world.Telegram.BotToken);
        }


        [Fact]
        public async Task Update_ATokenWithSpacesAround_IsAskedOfTelegramTrimmed_AndStoredTrimmed()
        {
            // A token pasted from a chat carries a line break now and then.
            _world.TelegramAnswers(_ => "hsm_alerts_bot");

            var result = await Service.UpdateAsync(new UpdateTelegramBotRequestDto { BotToken = $"  {TelegramBotHarness.NewToken}\n" }, _world.Caller);

            Assert.True(result.Success);
            _world.Bot.Verify(b => b.GetBotUsernameAsync(TelegramBotHarness.NewToken), Times.Once);
            Assert.Equal(TelegramBotHarness.NewToken, _world.Telegram.BotToken);
        }


        // ---- what the echo says when the restart fails ---------------------------------------


        [Fact]
        public async Task Update_ARestartThatFails_LeavesTheCredentialSaved_AndTheEchoSaysTheBotIsNotRunning()
        {
            _world.TelegramAnswers(_ => "hsm_oncall_bot");
            _world.RestartFails("An error (Internal Server Error) has been occurred while starting the Bot. Please check Bot configurations.");

            var result = await Service.UpdateAsync(
                new UpdateTelegramBotRequestDto { BotName = "hsm_oncall_bot", BotToken = TelegramBotHarness.NewToken }, _world.Caller);

            // The save landed. A failure here would read as "not saved" — and the same call
            // again would then be an update that changes nothing, and restart nothing.
            Assert.True(result.Success);
            Assert.Equal(new TelegramBotDto { BotName = "hsm_oncall_bot", IsEnabled = true, HasBotToken = true, IsRunning = false }, result.Value);
            Assert.Equal(TelegramBotHarness.NewToken, _world.Telegram.BotToken);
            Assert.Equal(["probe", "save", "restart"], _world.Steps);
            Assert.Equal(2, _world.Records.Count);
        }


        [Fact]
        public async Task Update_ARestartThatThrows_StillAnswersTheSavedCredential_AndTheEchoSaysTheBotIsNotRunning()
        {
            // The restart is the bot's to fail: whatever it throws, the save has landed, the
            // answer must not read as "not saved" — and a start that throws leaves no bot up.
            _world.TelegramAnswers(_ => "hsm_alerts_bot");
            _world.RestartThrows(new InvalidOperationException("the bot's internals"));

            var result = await Service.UpdateAsync(new UpdateTelegramBotRequestDto { BotToken = TelegramBotHarness.NewToken }, _world.Caller);

            Assert.True(result.Success);
            Assert.Equal(TelegramBotHarness.NewToken, _world.Telegram.BotToken);
            Assert.Equal(
                new TelegramBotDto { BotName = "hsm_alerts_bot", IsEnabled = true, HasBotToken = true, IsRunning = false },
                result.Value);
        }
    }
}
