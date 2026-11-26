using System;
using System.Net.Http;
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
using Telegram.Bot.Types;
using Xunit;

namespace HSMServer.Core.Tests.Notifications
{
    // #1830: the Telegram side of the bot-credential surface, on the real TelegramBot over an
    // injected Telegram client — asking who owns a token without touching the running bot,
    // the username a running bot remembers from its start, and the one-at-a-time start.
    [Collection("NLog isolated")]
    public sealed class TelegramBotProbeTests : IDisposable
    {
        private const string Token = "110201543:AAE-token-value-zzzzzzzzzzzzzzzzzzzzzzzzz";
        private const string OtherToken = "220301543:AAE-other-token-yyyyyyyyyyyyyyyyyyyy";

        private readonly MemoryTarget _memory = new() { Layout = "${message}" };
        private readonly LoggingConfiguration _previous = LogManager.Configuration;

        private readonly Mock<IChatsManager> _chats = new();
        private readonly Mock<IFolderManager> _folders = new();


        public TelegramBotProbeTests()
        {
            LogManager.Setup().LoadConfiguration(cfg => cfg.ForLogger().WriteTo(_memory));

            // No Telegram binding exists: the start's name synchronization asks no chat.
            _chats.Setup(c => c.GetValues()).Returns([]);
        }

        public void Dispose() => LogManager.Configuration = _previous;


        private string Logs => string.Join('\n', _memory.Logs);

        private static TelegramConfig ConfigWith(string token) =>
            new() { BotName = "hsm_alerts_bot", BotToken = token, IsRunning = true };

        private static void AnswerGetMe(Mock<ITelegramBotClient> client, User me) =>
            client.Setup(c => c.SendRequest<User>(It.IsAny<IRequest<User>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(me);

        private static void FailGetMe(Mock<ITelegramBotClient> client, Exception failure) =>
            client.Setup(c => c.SendRequest<User>(It.IsAny<IRequest<User>>(), It.IsAny<CancellationToken>()))
                .Throws(failure);

        // The client a START needs beyond GetMe: SetMyCommands and the stop's
        // DeleteWebhook/Close (true) and the polling loop's GetUpdates (nothing to deliver).
        private static void AnswerTheStart(Mock<ITelegramBotClient> client)
        {
            client.Setup(c => c.SendRequest<bool>(It.IsAny<IRequest<bool>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            client.Setup(c => c.SendRequest<Update[]>(It.IsAny<IRequest<Update[]>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);
        }

        private TelegramBot CreateBot(TelegramConfig config, Func<string, ITelegramBotClient> factory) =>
            new(_chats.Object, _folders.Object, config, factory);

        // A bot whose every client is the given one; `asked` receives the token each client
        // was built for.
        private TelegramBot CreateBot(TelegramConfig config, ITelegramBotClient client, System.Collections.Generic.List<string> asked = null) =>
            CreateBot(config, token =>
            {
                asked?.Add(token);
                return client;
            });


        // ---- asking who owns a token ------------------------------------------------


        [Fact]
        public async Task GetBotUsernameAsync_AnswersTheUsernameTelegramReports_ForTheTokenAsked()
        {
            var client = new Mock<ITelegramBotClient>();
            AnswerGetMe(client, new User { Username = "hsm_alerts_bot" });
            var asked = new System.Collections.Generic.List<string>();
            await using var bot = CreateBot(ConfigWith(Token), client.Object, asked);

            var probe = await bot.GetBotUsernameAsync(OtherToken);

            Assert.Equal(TelegramTokenVerdict.Accepted, probe.Verdict);
            Assert.Equal("hsm_alerts_bot", probe.Username);
            Assert.Equal([OtherToken], asked);
        }


        [Fact]
        public async Task GetBotUsernameAsync_TelegramRejectsTheToken_AnswersRejected_WithoutTheToken()
        {
            // The library does not put the request URL into exception messages today; the
            // probe must not be the place where that starts to leak (#1824).
            var client = new Mock<ITelegramBotClient>();
            FailGetMe(client, new ApiRequestException($"Request failed for {OtherToken}", 401));
            await using var bot = CreateBot(ConfigWith(Token), client.Object);

            var probe = await bot.GetBotUsernameAsync(OtherToken);

            Assert.Equal(TelegramTokenVerdict.Rejected, probe.Verdict);
            Assert.Contains("Request failed for", probe.Reason);
            Assert.DoesNotContain(OtherToken, probe.Reason);
            Assert.DoesNotContain(OtherToken, Logs);
            Assert.Contains("Request failed for", Logs);
        }


        [Fact]
        public async Task GetBotUsernameAsync_TokenOfTheWrongShape_AnswersRejected()
        {
            // The client itself refuses a token that is not shaped like one — no request
            // leaves the server. It is the token that is wrong, not the road to Telegram.
            await using var bot = CreateBot(ConfigWith(Token), _ => throw new ArgumentException("Bad bot token format."));

            var probe = await bot.GetBotUsernameAsync("not-a-token");

            Assert.Equal(TelegramTokenVerdict.Rejected, probe.Verdict);
        }


        [Fact]
        public async Task GetBotUsernameAsync_TelegramIsNotReached_AnswersUnreachable_WithoutTheToken()
        {
            var client = new Mock<ITelegramBotClient>();
            FailGetMe(client, new HttpRequestException($"GET https://api.telegram.org/bot{OtherToken}/getMe timed out"));
            await using var bot = CreateBot(ConfigWith(Token), client.Object);

            var probe = await bot.GetBotUsernameAsync(OtherToken);

            Assert.Equal(TelegramTokenVerdict.Unreachable, probe.Verdict);
            Assert.DoesNotContain(OtherToken, probe.Reason);
            Assert.DoesNotContain(OtherToken, Logs);
        }


        [Fact]
        public async Task GetBotUsernameAsync_TelegramAsksToRetryLater_AnswersUnreachable()
        {
            // A rate limit is Telegram answering — but not about the token: the same
            // request a moment later is accepted, so the token is not the problem.
            var client = new Mock<ITelegramBotClient>();
            FailGetMe(client, new ApiRequestException("Too Many Requests", 429));
            await using var bot = CreateBot(ConfigWith(Token), client.Object);

            var probe = await bot.GetBotUsernameAsync(OtherToken);

            Assert.Equal(TelegramTokenVerdict.Unreachable, probe.Verdict);
        }


        [Fact]
        public async Task GetBotUsernameAsync_ARefusedToken_LeavesTheRunningBotAlone()
        {
            // The probe goes through a client of its own: the running one is neither stopped
            // nor rebuilt, whatever Telegram says about the token asked.
            var running = new Mock<ITelegramBotClient>();
            AnswerTheStart(running);
            AnswerGetMe(running, new User { Username = "hsm_alerts_bot" });

            var refused = new Mock<ITelegramBotClient>();
            FailGetMe(refused, new ApiRequestException("Unauthorized", 401));

            var builtFor = new System.Collections.Generic.List<string>();
            await using var bot = CreateBot(ConfigWith(Token), token =>
            {
                builtFor.Add(token);
                return token == Token ? running.Object : refused.Object;
            });

            Assert.Equal(string.Empty, await bot.StartBotAsync());

            var probe = await bot.GetBotUsernameAsync(OtherToken);

            Assert.Equal(TelegramTokenVerdict.Rejected, probe.Verdict);
            Assert.True(bot.IsBotRunning);
            Assert.Equal("hsm_alerts_bot", bot.UsernameOfRunningBot(Token));
            Assert.Equal([Token, OtherToken], builtFor);

            // SetMyCommands of the start, and nothing since: a stop would ask for two more.
            running.Verify(c => c.SendRequest<bool>(It.IsAny<IRequest<bool>>(), It.IsAny<CancellationToken>()), Times.Once);
        }


        // ---- the start ----------------------------------------------------------------


        [Fact]
        public async Task AStartedBot_RemembersTheUsernameTelegramGave_ForItsOwnTokenOnly_AndForgetsItOnStop()
        {
            // A bot name is checked against the username of the token. A running bot has
            // just been told that username, so asking Telegram again is one call too many.
            var client = new Mock<ITelegramBotClient>();
            AnswerTheStart(client);
            AnswerGetMe(client, new User { Username = "hsm_alerts_bot" });
            await using var bot = CreateBot(ConfigWith(Token), client.Object);

            Assert.Null(bot.UsernameOfRunningBot(Token));

            Assert.Equal(string.Empty, await bot.StartBotAsync());

            Assert.Equal("hsm_alerts_bot", bot.UsernameOfRunningBot(Token));

            // Another token is not the one the bot runs on: nothing is known about it. The
            // saved token may have moved on while the bot keeps the one it started with.
            Assert.Null(bot.UsernameOfRunningBot(OtherToken));

            await bot.StopBotAsync();

            Assert.Null(bot.UsernameOfRunningBot(Token));
        }


        [Fact]
        public async Task StartBotAsync_RunsOnTheTokenStoredNow_NotTheOneTheRunningClientWasBuiltFor()
        {
            // The web page and the management surface save alike: the next start reads the
            // settings as they are, so a saved token is the one the bot comes up on.
            var client = new Mock<ITelegramBotClient>();
            AnswerTheStart(client);
            AnswerGetMe(client, new User { Username = "hsm_alerts_bot" });

            var builtFor = new System.Collections.Generic.List<string>();
            var config = ConfigWith(Token);
            await using var bot = CreateBot(config, token =>
            {
                builtFor.Add(token);
                return client.Object;
            });

            Assert.Equal(string.Empty, await bot.StartBotAsync());

            // Saved, not restarted yet: the bot keeps the client it has.
            config.BotToken = OtherToken;
            Assert.Equal("hsm_alerts_bot", bot.UsernameOfRunningBot(Token));
            Assert.Null(bot.UsernameOfRunningBot(OtherToken));

            Assert.Equal(string.Empty, await bot.StartBotAsync());

            Assert.Equal([Token, OtherToken], builtFor);
            Assert.True(bot.IsBotRunning);
            Assert.Equal("hsm_alerts_bot", bot.UsernameOfRunningBot(OtherToken));
            Assert.Null(bot.UsernameOfRunningBot(Token));
        }


        [Fact]
        public async Task StartBotAsync_AStartTelegramRefuses_RemembersNoUsername()
        {
            var client = new Mock<ITelegramBotClient>();
            AnswerTheStart(client);
            FailGetMe(client, new ApiRequestException("Unauthorized", 401));
            await using var bot = CreateBot(ConfigWith(Token), client.Object);

            await bot.StartBotAsync();

            Assert.False(bot.IsBotRunning);
            Assert.Null(bot.UsernameOfRunningBot(Token));
        }

        [Fact]
        public async Task StartBotAsync_AFailureAfterGetMe_LeavesTheBotStopped_AndAnswersTheErrorWithoutTheToken()
        {
            // A start that breaks off half-way must not leave a client that counts as
            // running and receives nothing: the status the management surface echoes would
            // then be a lie.
            var client = new Mock<ITelegramBotClient>();
            AnswerGetMe(client, new User { Username = "hsm_alerts_bot" });
            client.Setup(c => c.SendRequest<bool>(It.IsAny<IRequest<bool>>(), It.IsAny<CancellationToken>()))
                .Throws(new ApiRequestException($"Request failed for {Token}", 500));
            await using var bot = CreateBot(ConfigWith(Token), client.Object);

            var message = await bot.StartBotAsync();

            Assert.False(bot.IsBotRunning);
            Assert.Contains("error", message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(Token, message);
            Assert.DoesNotContain(Token, Logs);
        }


        [Fact]
        public async Task DisposeAsync_AStartInFlight_IsNotHeldByIt_AndLeavesNoBotBehind()
        {
            // The server may stop while a start is waiting for Telegram. The stop cancels
            // that wait, so the start lets go of the gate at once — disposing must not sit
            // on the gate until Telegram answers or the request times out.
            var asking = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var client = new Mock<ITelegramBotClient>();
            AnswerTheStart(client);
            client.Setup(c => c.SendRequest<User>(It.IsAny<IRequest<User>>(), It.IsAny<CancellationToken>()))
                .Returns(async (IRequest<User> _, CancellationToken cancellation) =>
                {
                    asking.TrySetResult();
                    await Task.Delay(Timeout.Infinite, cancellation);
                    return new User { Username = "hsm_alerts_bot" };
                });
            var bot = CreateBot(ConfigWith(Token), client.Object);

            var start = bot.StartBotAsync();
            await asking.Task;

            await bot.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

            Assert.False(bot.IsBotRunning);
            Assert.NotEqual(string.Empty, await start.WaitAsync(TimeSpan.FromSeconds(5)));
        }


        [Fact]
        public async Task StartBotAsync_AfterDispose_StartsNothing()
        {
            var client = new Mock<ITelegramBotClient>();
            AnswerTheStart(client);
            var bot = CreateBot(ConfigWith(Token), client.Object);
            await bot.DisposeAsync();

            Assert.Equal(TelegramBot.BotIsDisposedError, await bot.StartBotAsync());
            client.Verify(c => c.SendRequest<User>(It.IsAny<IRequest<User>>(), It.IsAny<CancellationToken>()), Times.Never);

            // Disposing twice is harmless.
            await bot.DisposeAsync();
        }


        [Fact]
        public async Task StartBotAsync_TwoStartsAtOnce_RunOneAfterTheOther()
        {
            // The web UI's Restart button and the management surface's token change can
            // ask for a start at the same moment. Two clients receiving at once would answer
            // every Telegram update twice, so the second start waits for the first.
            var firstIsAsking = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseFirst = new TaskCompletionSource<User>(TaskCreationOptions.RunContinuationsAsynchronously);
            var asked = 0;

            var client = new Mock<ITelegramBotClient>();
            AnswerTheStart(client);
            client.Setup(c => c.SendRequest<User>(It.IsAny<IRequest<User>>(), It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    var attempt = Interlocked.Increment(ref asked);
                    return attempt == 1 ? WaitFirst() : Task.FromResult(new User { Username = "hsm_alerts_bot" });

                    async Task<User> WaitFirst()
                    {
                        firstIsAsking.TrySetResult();
                        return await releaseFirst.Task;
                    }
                });
            await using var bot = CreateBot(ConfigWith(Token), client.Object);

            var first = bot.StartBotAsync();
            await firstIsAsking.Task;

            var second = bot.StartBotAsync();
            await Task.Delay(200);

            // The first start still holds the floor: Telegram has been asked once.
            Assert.Equal(1, Volatile.Read(ref asked));

            releaseFirst.SetResult(new User { Username = "hsm_alerts_bot" });

            Assert.Equal([string.Empty, string.Empty], await Task.WhenAll(first, second));
            Assert.Equal(2, Volatile.Read(ref asked));
            Assert.True(bot.IsBotRunning);
        }
    }
}
