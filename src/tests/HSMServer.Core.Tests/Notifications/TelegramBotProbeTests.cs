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
using Telegram.Bot.Args;
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
            Assert.Equal(TelegramBot.BotIsDisposedError, await start.WaitAsync(TimeSpan.FromSeconds(5)));

            // The end of the bot is no refusal of its credentials: nothing of the kind is logged.
            Assert.DoesNotContain("Invalid credentials", Logs);
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


        // ---- a previous client that does not stop cleanly -------------------------------


        [Fact]
        public async Task StartBotAsync_WhenThePreviousClientDoesNotStopCleanly_StillStartsOnTheStoredToken()
        {
            // A token regenerated in BotFather dies before it is replaced here, and Telegram
            // then refuses the old client's goodbye. That client is dropped either way: the
            // start on the stored token must not depend on it. The management surface has no
            // second click — an update that changes nothing restarts nothing — so this is the
            // only place the bot comes back.
            var previous = new Mock<ITelegramBotClient>();
            AnswerGetMe(previous, new User { Username = "hsm_alerts_bot" });
            var goodbyes = 0;
            previous.Setup(c => c.SendRequest<bool>(It.IsAny<IRequest<bool>>(), It.IsAny<CancellationToken>()))
                .Returns(() => ++goodbyes == 1
                    ? Task.FromResult(true) // SetMyCommands of the first start
                    : Task.FromException<bool>(new ApiRequestException($"Request failed for {Token}", 401)));

            var next = new Mock<ITelegramBotClient>();
            AnswerTheStart(next);
            AnswerGetMe(next, new User { Username = "hsm_alerts_bot" });

            var config = ConfigWith(Token);
            await using var bot = CreateBot(config, token => token == Token ? previous.Object : next.Object);
            Assert.Equal(string.Empty, await bot.StartBotAsync());

            config.BotToken = OtherToken;
            var message = await bot.StartBotAsync();

            Assert.Equal(string.Empty, message);
            Assert.True(bot.IsBotRunning);
            Assert.Equal("hsm_alerts_bot", bot.UsernameOfRunningBot(OtherToken));

            // The unclean goodbye is reported — as a warning, without the token it concerned.
            Assert.Contains("did not stop cleanly", Logs);
            Assert.DoesNotContain(Token, Logs);
        }


        [Fact]
        public async Task StopBotAsync_AFailingGoodbye_AnswersTheErrorWithoutTheToken()
        {
            var client = new Mock<ITelegramBotClient>();
            AnswerGetMe(client, new User { Username = "hsm_alerts_bot" });
            var asked = 0;
            client.Setup(c => c.SendRequest<bool>(It.IsAny<IRequest<bool>>(), It.IsAny<CancellationToken>()))
                .Returns(() => ++asked == 1
                    ? Task.FromResult(true) // SetMyCommands of the start
                    : Task.FromException<bool>(new ApiRequestException($"Request failed for {Token}", 500)));
            await using var bot = CreateBot(ConfigWith(Token), client.Object);
            await bot.StartBotAsync();

            var message = await bot.StopBotAsync();

            Assert.Contains("while stopping", message);
            Assert.DoesNotContain(Token, message);
            Assert.False(bot.IsBotRunning);
        }


        // ---- who disposes what -----------------------------------------------------------


        [Fact]
        public async Task GetBotUsernameAsync_DisposesTheClientItBuilt_AndLeavesTheRunningOneAlone()
        {
            // The probe's client is used once: it takes its HttpClient with it. The running
            // client stays for as long as the bot runs.
            var client = new Mock<ITelegramBotClient>();
            var owned = client.As<IDisposable>();
            AnswerTheStart(client);
            AnswerGetMe(client, new User { Username = "hsm_alerts_bot" });
            await using var bot = CreateBot(ConfigWith(Token), client.Object);
            await bot.StartBotAsync();
            owned.Verify(d => d.Dispose(), Times.Never);

            await bot.GetBotUsernameAsync(OtherToken);

            owned.Verify(d => d.Dispose(), Times.Once);
            Assert.True(bot.IsBotRunning);
        }


        [Fact]
        public async Task TheRealClientsWrapper_ForwardsToTheClientItWraps_AndDisposesItsHttpClient()
        {
            // Every Telegram call of a started bot goes through the wrapper, and no test
            // reaches the network: it is driven here over a client of its own.
            var inner = new Mock<ITelegramBotClient>();
            inner.SetupGet(c => c.BotId).Returns(110201543);
            inner.SetupGet(c => c.LocalBotServer).Returns(true);
            inner.SetupProperty(c => c.Timeout, TimeSpan.FromMinutes(5));
            AnswerGetMe(inner, new User { Username = "hsm_alerts_bot" });

            var handler = new DisposeRecordingHandler();
            var wrapped = new TelegramBot.OwnedClient(inner.Object, new HttpClient(handler));

            Assert.Equal(110201543, wrapped.BotId);
            Assert.True(wrapped.LocalBotServer);

            wrapped.Timeout = TimeSpan.FromMinutes(1);
            Assert.Equal(TimeSpan.FromMinutes(1), inner.Object.Timeout);

            static ValueTask OnMaking(ITelegramBotClient client, ApiRequestEventArgs args, CancellationToken cancellation) =>
                ValueTask.CompletedTask;
            wrapped.OnMakingApiRequest += OnMaking;
            wrapped.OnMakingApiRequest -= OnMaking;

            Assert.Equal("hsm_alerts_bot", (await wrapped.GetMe(CancellationToken.None)).Username);
            inner.Verify(c => c.SendRequest<User>(It.IsAny<IRequest<User>>(), It.IsAny<CancellationToken>()), Times.Once);

            Assert.False(handler.Disposed);

            // The HttpClient went with the wrapper.
            wrapped.Dispose();

            Assert.True(handler.Disposed);
        }


        // Says whether the HttpClient above it was disposed — without sending anything.
        private sealed class DisposeRecordingHandler : HttpClientHandler
        {
            public bool Disposed { get; private set; }

            protected override void Dispose(bool disposing)
            {
                Disposed = true;
                base.Dispose(disposing);
            }
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
            var firstIsPastGetMe = 0;
            var firstWasPastGetMeWhenAskedAgain = -1;

            var client = new Mock<ITelegramBotClient>();
            client.Setup(c => c.SendRequest<bool>(It.IsAny<IRequest<bool>>(), It.IsAny<CancellationToken>()))
                .Callback(() => Interlocked.Exchange(ref firstIsPastGetMe, 1))
                .ReturnsAsync(true);
            client.Setup(c => c.SendRequest<Update[]>(It.IsAny<IRequest<Update[]>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);
            client.Setup(c => c.SendRequest<User>(It.IsAny<IRequest<User>>(), It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    var attempt = Interlocked.Increment(ref asked);

                    if (attempt == 1)
                        return WaitFirst();

                    firstWasPastGetMeWhenAskedAgain = Volatile.Read(ref firstIsPastGetMe);
                    return Task.FromResult(new User { Username = "hsm_alerts_bot" });

                    async Task<User> WaitFirst()
                    {
                        firstIsAsking.TrySetResult();
                        return await releaseFirst.Task;
                    }
                });
            await using var bot = CreateBot(ConfigWith(Token), client.Object);

            var first = bot.StartBotAsync();
            await firstIsAsking.Task;

            // GetMe has not answered yet: the bot has no username to remember.
            Assert.Null(bot.UsernameOfRunningBot(Token));

            var second = bot.StartBotAsync();

            // The first start still holds the floor: the second is waiting and Telegram has
            // been asked once.
            Assert.False(second.IsCompleted);
            Assert.Equal(1, Volatile.Read(ref asked));

            releaseFirst.SetResult(new User { Username = "hsm_alerts_bot" });

            Assert.Equal([string.Empty, string.Empty], await Task.WhenAll(first, second));
            Assert.Equal(2, Volatile.Read(ref asked));
            Assert.Equal(1, firstWasPastGetMeWhenAskedAgain);
            Assert.True(bot.IsBotRunning);
        }
    }
}
