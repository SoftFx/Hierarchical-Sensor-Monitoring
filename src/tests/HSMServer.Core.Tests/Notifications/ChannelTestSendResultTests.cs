using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Folders;
using HSMServer.Notifications;
using HSMServer.Notifications.Chats;
using HSMServer.ServerConfiguration;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.Notifications
{
    // #1826, seam A: a chat test message answers its delivery outcome. The webhook channels
    // run against a fake handler (success, terminal status, network failure — one attempt, no
    // backoff waits: the internal ctor takes zero backoff); Telegram runs through the
    // injectable-client seam of #1824.
    //
    // NLog-isolated: the failing sends write real ERROR entries through the process-global
    // NLog config, which would pollute the capture of any config-swapping test running in a
    // parallel collection (the collection serializes its members).
    [Collection("NLog isolated")]
    public sealed class ChannelTestSendResultTests
    {
        private static readonly Chat SlackChat = new(new ChatEntity
        {
            Id = Guid.NewGuid().ToByteArray(),
            Name = "ops",
            SlackWebhookUrl = "https://hooks.slack.com/services/X",
        });

        private static readonly Chat MattermostChat = new(new ChatEntity
        {
            Id = Guid.NewGuid().ToByteArray(),
            Name = "ops",
            MattermostWebhookUrl = "https://mattermost.example/hooks/X",
        });


        [Fact]
        public async Task SlackTest_StatusCode200_AnswersDelivered()
        {
            var channel = BuildSlackChannel(_ => new HttpResponseMessage(HttpStatusCode.OK));

            var result = await channel.SendTestAsync(SlackChat);

            Assert.True(result.Delivered);
            Assert.Null(result.Error);
        }

        [Fact]
        public async Task SlackTest_TerminalStatus_AnswersTheError()
        {
            var channel = BuildSlackChannel(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

            var result = await channel.SendTestAsync(SlackChat);

            Assert.False(result.Delivered);
            Assert.Contains("NotFound", result.Error);
        }

        [Fact]
        public async Task SlackTest_NetworkFailureOnEveryAttempt_AnswersTheErrorWithoutThrowing()
        {
            var channel = BuildSlackChannel(_ => throw new HttpRequestException("connection refused"));

            var result = await channel.SendTestAsync(SlackChat);

            Assert.False(result.Delivered);
            Assert.Contains("connection refused", result.Error);
        }

        [Fact]
        public async Task MattermostTest_AnswersItsOutcome()
        {
            var ok = BuildMattermostChannel(_ => new HttpResponseMessage(HttpStatusCode.OK));
            var broken = BuildMattermostChannel(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));

            Assert.True((await ok.SendTestAsync(MattermostChat)).Delivered);
            Assert.False((await broken.SendTestAsync(MattermostChat)).Delivered);
        }

        [Fact]
        public async Task TelegramTest_ClientSends_AnswersDelivered()
        {
            // await using: StartBotAsync leaves a live receive loop; undisposed, it logs
            // deserialization errors forever and pollutes every NLog capture in the process.
            await using var bot = await BuildStartedBotAsync(BotApiOk);

            var chat = new Chat(new ChatEntity
            {
                Id = Guid.NewGuid().ToByteArray(),
                Name = "ops",
                TelegramChatId = 12345L,
                TelegramType = (byte)ConnectedChatType.TelegramPrivate,
            });

            var result = await bot.SendTestWithResultAsync(chat);

            Assert.True(result.Delivered);
        }

        [Fact]
        public async Task TelegramTest_ClientFails_AnswersTheError()
        {
            // The bot starts fine; sendMessage answers the Bot API's 400 shape
            // ({"ok":false,"error_code":400,"description":"Bad Request: ..."}).
            await using var bot = await BuildStartedBotAsync(request =>
                request.RequestUri.AbsolutePath.EndsWith("sendMessage")
                    ? new HttpResponseMessage(HttpStatusCode.BadRequest)
                    {
                        Content = new StringContent("{\"ok\":false,\"error_code\":400,\"description\":\"Bad Request: chat not found\"}", System.Text.Encoding.UTF8, "application/json"),
                    }
                    : BotApiOk(request));

            var chat = new Chat(new ChatEntity
            {
                Id = Guid.NewGuid().ToByteArray(),
                Name = "ops",
                TelegramChatId = 12345L,
                TelegramType = (byte)ConnectedChatType.TelegramPrivate,
            });

            var result = await bot.SendTestWithResultAsync(chat);

            Assert.False(result.Delivered);
            Assert.NotNull(result.Error);
        }


        private static SlackNotificationChannel BuildSlackChannel(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
            new(new Mock<IChatsManager>().Object, new HttpClient(new RespondingHandler(respond)),
                maxRetryAttempts: 1, initialBackoff: TimeSpan.Zero, requestTimeout: TimeSpan.FromSeconds(5));

        private static MattermostNotificationChannel BuildMattermostChannel(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
            new(new Mock<IChatsManager>().Object, new HttpClient(new RespondingHandler(respond)),
                maxRetryAttempts: 1, initialBackoff: TimeSpan.Zero, requestTimeout: TimeSpan.FromSeconds(5));

        // A bot whose client is the library's real one over a local fake transport — the
        // production serialization path runs, the network does not. Started for real: the
        // test-send answers "not running" until StartBotAsync has run.
        private static async Task<TelegramBot> BuildStartedBotAsync(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            var config = new TelegramConfig { BotName = "hsm_alerts_bot", BotToken = "110201543:AAE-test-token", IsRunning = true };

            var chats = new Mock<IChatsManager>();
            chats.Setup(c => c.GetValues()).Returns(new List<Chat>());

            var bot = new TelegramBot(chats.Object, new Mock<IFolderManager>().Object, config,
                token => new Telegram.Bot.TelegramBotClient(token, new HttpClient(new RespondingHandler(respond))));

            Assert.Equal(string.Empty, await bot.StartBotAsync());

            return bot;
        }

        // The Bot API payloads for the three calls the start and the test send make — the
        // method name is the last path segment of the request URL.
        private static HttpResponseMessage BotApiOk(HttpRequestMessage request)
        {
            var method = request.RequestUri.AbsolutePath.TrimEnd('/')[..].Split('/')[^1];

            var json = method switch
            {
                "getMe" => "{\"ok\":true,\"result\":{\"id\":1,\"is_bot\":true,\"first_name\":\"HSM\",\"username\":\"hsm_alerts_bot\"}}",
                "setMyCommands" => "{\"ok\":true,\"result\":true}",
                _ => "{\"ok\":true,\"result\":{\"message_id\":1,\"chat\":{\"id\":12345,\"type\":\"private\"}}}",
            };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
            };
        }


        private sealed class RespondingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                Task.FromResult(respond(request));
        }
    }
}
