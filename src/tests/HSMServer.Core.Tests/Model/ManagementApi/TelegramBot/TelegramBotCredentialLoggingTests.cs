using System;
using System.Threading.Tasks;
using Moq;
using NLog;
using NLog.Config;
using NLog.Targets;
using HSMServer.Model.ManagementApi.TelegramBot;
using HSMServer.Notifications;
using Xunit;

namespace HSMServer.Core.Tests.Model.ManagementApi.TelegramBot
{
    // #1830: the token is a secret that never reaches the log (#1824). The Telegram side
    // wipes it from its own texts (TelegramBotProbeTests); these pin the lines the surface
    // itself writes — the save's description and the failed restart — on every way a write
    // can go.
    [Collection("NLog isolated")]
    public sealed class TelegramBotCredentialLoggingTests : IDisposable
    {
        private readonly MemoryTarget _memory = new() { Layout = "${message}" };
        private readonly LoggingConfiguration _previous = LogManager.Configuration;


        public TelegramBotCredentialLoggingTests() =>
            LogManager.Setup().LoadConfiguration(cfg => cfg.ForLogger().WriteTo(_memory));

        public void Dispose() => LogManager.Configuration = _previous;


        private string Logs => string.Join('\n', _memory.Logs);

        private static UpdateTelegramBotRequestDto NewCredential { get; } =
            new() { BotName = "hsm_oncall_bot", BotToken = TelegramBotHarness.NewToken };


        [Fact]
        public async Task Update_ASavedToken_LogsThatItChanged_NeverTheToken()
        {
            var world = new TelegramBotHarness();
            world.TelegramAnswers(_ => "hsm_oncall_bot");

            await world.CreateService().UpdateAsync(NewCredential, world.Caller);

            Assert.Contains("admin is changing Telegram config:", Logs);
            Assert.Contains("BotToken: changed", Logs);
            Assert.Contains("BotName: hsm_alerts_bot -> hsm_oncall_bot", Logs);
            Assert.DoesNotContain(TelegramBotHarness.OldToken, Logs);
            Assert.DoesNotContain(TelegramBotHarness.NewToken, Logs);
        }


        [Fact]
        public async Task Update_ARefusedToken_LogsNothingOfIt()
        {
            var world = new TelegramBotHarness();
            world.TelegramRefuses(_ => TelegramTokenProbe.Rejected("Unauthorized"));

            await world.CreateService().UpdateAsync(NewCredential, world.Caller);

            // The refusal reaches the caller alone; the Telegram side logs its own line.
            Assert.DoesNotContain("changing Telegram config", Logs);
            Assert.DoesNotContain(TelegramBotHarness.OldToken, Logs);
            Assert.DoesNotContain(TelegramBotHarness.NewToken, Logs);
        }


        [Fact]
        public async Task Update_ABotThatDidNotStartAfterTheSave_IsLoggedAsSuch_WithTheBotsOwnText()
        {
            var world = new TelegramBotHarness();
            world.TelegramAnswers(_ => "hsm_alerts_bot");
            world.RestartFails("An error (Internal Server Error) has been occurred while starting the Bot.");

            await world.CreateService().UpdateAsync(new UpdateTelegramBotRequestDto { BotToken = TelegramBotHarness.NewToken }, world.Caller);

            Assert.Contains("saved, but the bot did not start: An error (Internal Server Error) has been occurred while starting the Bot.", Logs);
            Assert.Contains("BotToken: changed", Logs);
            Assert.DoesNotContain(TelegramBotHarness.OldToken, Logs);
            Assert.DoesNotContain(TelegramBotHarness.NewToken, Logs);
        }
    }
}
