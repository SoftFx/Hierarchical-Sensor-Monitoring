using System;
using System.Threading;
using System.Threading.Tasks;
using HSMServer.Folders;
using HSMServer.Notifications;
using HSMServer.Notifications.Chats;
using HSMServer.ServerConfiguration;
using Moq;
using NLog;
using NLog.Config;
using NLog.Targets;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Requests.Abstractions;
using Xunit;

namespace HSMServer.Core.Tests.Notifications
{
    // #1824: a failed bot start must keep the bot token out of the log and out of the message
    // returned to the caller. The start path is driven through an injected Telegram client,
    // so no network is involved.
    [Collection("NLog isolated")]
    public sealed class TelegramBotStartFailureLoggingTests : IDisposable
    {
        private const string Token = "110201543:AAE-token-value-zzzzzzzzzzzzzzzzzzzzz";

        private readonly MemoryTarget _memory = new() { Layout = "${message}" };
        private readonly LoggingConfiguration _previous = LogManager.Configuration;

        public TelegramBotStartFailureLoggingTests() =>
            LogManager.Setup().LoadConfiguration(cfg => cfg.ForLogger().WriteTo(_memory));

        public void Dispose() => LogManager.Configuration = _previous;


        private string Logs => string.Join('\n', _memory.Logs);

        private static TelegramConfig ConfigWith(string token) =>
            new() { BotName = "hsm_alerts_bot", BotToken = token, IsRunning = true };

        private static TelegramBot CreateBot(TelegramConfig config, ITelegramBotClient client) =>
            new(new Mock<IChatsManager>().Object, new Mock<IFolderManager>().Object, config, _ => client);


        // GetMe is an extension method over ITelegramBotClient in this library version, so the
        // mock is set on the core SendRequest the extension forwards to. Throws (not ThrowsAsync):
        // with It.IsAnyType the async wrapper miscasts the returned task in the proxy.
        private static Mock<ITelegramBotClient> ClientFailingWith(Exception failure)
        {
            var client = new Mock<ITelegramBotClient>();
            client.Setup(c => c.SendRequest<It.IsAnyType>(It.IsAny<IRequest<It.IsAnyType>>(), It.IsAny<CancellationToken>()))
                  .Throws(failure);
            return client;
        }


        [Fact]
        public async Task StartBotAsync_InvalidCredentials_ReportsFailureWithoutTheToken()
        {
            var bot = CreateBot(ConfigWith(Token), ClientFailingWith(new ApiRequestException("Unauthorized", 401)).Object);

            var message = await bot.StartBotAsync();

            Assert.False(bot.IsBotRunning);
            Assert.Contains("error", message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Unauthorized", Logs);
            Assert.DoesNotContain(Token, Logs);
        }


        [Fact]
        public async Task StartBotAsync_ExceptionMessageCarriesTheToken_LogsAndReturnsItWithoutTheToken()
        {
            var bot = CreateBot(ConfigWith(Token), ClientFailingWith(new ApiRequestException($"Request failed for {Token}", 401)).Object);

            var message = await bot.StartBotAsync();

            Assert.DoesNotContain(Token, Logs);
            Assert.DoesNotContain(Token, message);
        }


        [Fact]
        public async Task StartBotAsync_TokenReplacedWhileTheRequestIsInFlight_StillRedactsTheTokenTheStartUsed()
        {
            var config = ConfigWith(Token);
            var client = new Mock<ITelegramBotClient>();
            // The admin saves a new token while the start request is in flight; the failure then
            // carries the token the start was attempted WITH.
            client.Setup(c => c.SendRequest<It.IsAnyType>(It.IsAny<IRequest<It.IsAnyType>>(), It.IsAny<CancellationToken>()))
                  .Callback(() => config.BotToken = "220301543:AAE-replaced-meanwhile-xxxxxxxxxxx")
                  .Throws(new ApiRequestException($"Request failed for {Token}", 401));

            var bot = CreateBot(config, client.Object);

            var message = await bot.StartBotAsync();

            Assert.DoesNotContain(Token, Logs);
            Assert.DoesNotContain(Token, message);
        }
    }
}
