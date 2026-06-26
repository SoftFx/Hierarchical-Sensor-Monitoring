using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Managers;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.Notifications;
using HSMServer.Notifications;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.Notifications
{
    public class SlackChannelTests
    {
        private const string WebhookUrl = "https://hooks.slack.com/services/X";
        private static readonly TimeSpan TestBackoff = TimeSpan.FromMilliseconds(1);


        [Fact]
        public async Task DeliverAsync_SlackPolicy_PostsToWebhookAndFiresEvents()
        {
            var (manager, destinationId) = SeedDestination(send: true);
            var handler = new StubHandler(HttpStatusCode.OK);

            var channel = new SlackNotificationChannel(manager, new HttpClient(handler),
                maxRetryAttempts: 3, initialBackoff: TestBackoff, requestTimeout: TimeSpan.FromSeconds(5));

            int sending = 0, sended = 0;
            channel.MessageSending += () => sending++;
            channel.MessageSended += (_, _) => sended++;

            await channel.DeliverAsync(BuildMessage(destinationId, NotificationKind.Slack));

            Assert.Equal(1, handler.Calls);
            Assert.Equal(WebhookUrl, handler.LastRequestUri);
            Assert.Equal(1, sending);
            Assert.Equal(1, sended);
        }

        [Fact]
        public async Task DeliverAsync_TelegramPolicy_DoesNotPost()
        {
            var (manager, destinationId) = SeedDestination(send: true);
            var handler = new StubHandler(HttpStatusCode.OK);

            var channel = new SlackNotificationChannel(manager, new HttpClient(handler),
                maxRetryAttempts: 1, initialBackoff: TestBackoff, requestTimeout: TimeSpan.FromSeconds(5));

            await channel.DeliverAsync(BuildMessage(destinationId, NotificationKind.Telegram));

            Assert.Equal(0, handler.Calls);
        }

        [Fact]
        public async Task DeliverAsync_DisabledDestination_Skipped()
        {
            var (manager, destinationId) = SeedDestination(send: false);
            var handler = new StubHandler(HttpStatusCode.OK);

            var channel = new SlackNotificationChannel(manager, new HttpClient(handler),
                maxRetryAttempts: 1, initialBackoff: TestBackoff, requestTimeout: TimeSpan.FromSeconds(5));

            await channel.DeliverAsync(BuildMessage(destinationId, NotificationKind.Slack));

            Assert.Equal(0, handler.Calls);
        }

        [Fact]
        public async Task DeliverAsync_4xxResponse_TerminalNoRetry()
        {
            var (manager, destinationId) = SeedDestination(send: true);
            var handler = new StubHandler(HttpStatusCode.BadRequest);

            var channel = new SlackNotificationChannel(manager, new HttpClient(handler),
                maxRetryAttempts: 3, initialBackoff: TestBackoff, requestTimeout: TimeSpan.FromSeconds(5));

            int errors = 0;
            channel.ErrorHandled += _ => errors++;

            await channel.DeliverAsync(BuildMessage(destinationId, NotificationKind.Slack));

            Assert.Equal(1, handler.Calls);
            Assert.Equal(1, errors);
        }

        [Fact]
        public async Task DeliverAsync_5xxResponse_RetriesThenSucceeds()
        {
            var (manager, destinationId) = SeedDestination(send: true);
            var handler = new StubHandler(HttpStatusCode.InternalServerError, HttpStatusCode.OK);

            var channel = new SlackNotificationChannel(manager, new HttpClient(handler),
                maxRetryAttempts: 3, initialBackoff: TestBackoff, requestTimeout: TimeSpan.FromSeconds(5));

            int sended = 0;
            channel.MessageSended += (_, _) => sended++;

            await channel.DeliverAsync(BuildMessage(destinationId, NotificationKind.Slack));

            Assert.Equal(2, handler.Calls);
            Assert.Equal(1, sended);
        }

        [Fact]
        public async Task DeliverAsync_UnknownDestination_SkippedWithoutErrorEvent()
        {
            var manager = BuildManager();
            var handler = new StubHandler(HttpStatusCode.OK);

            var channel = new SlackNotificationChannel(manager, new HttpClient(handler),
                maxRetryAttempts: 1, initialBackoff: TestBackoff, requestTimeout: TimeSpan.FromSeconds(5));

            int errors = 0;
            channel.ErrorHandled += _ => errors++;

            await channel.DeliverAsync(BuildMessage(Guid.NewGuid(), NotificationKind.Slack));

            Assert.Equal(0, handler.Calls);
            Assert.Equal(0, errors);
        }

        [Fact]
        public async Task DeliverAsync_All5xx_RetriesUntilLimitThenErrors()
        {
            var (manager, destinationId) = SeedDestination(send: true);
            var handler = new StubHandler(HttpStatusCode.ServiceUnavailable);

            var channel = new SlackNotificationChannel(manager, new HttpClient(handler),
                maxRetryAttempts: 3, initialBackoff: TestBackoff, requestTimeout: TimeSpan.FromSeconds(5));

            int errors = 0;
            int sended = 0;
            channel.ErrorHandled += _ => errors++;
            channel.MessageSended += (_, _) => sended++;

            await channel.DeliverAsync(BuildMessage(destinationId, NotificationKind.Slack));

            Assert.Equal(3, handler.Calls);
            Assert.Equal(0, sended);
            Assert.True(errors >= 1);
        }

        [Fact]
        public async Task DeliverAsync_ExceptionInSend_DoesNotPropagate()
        {
            var (manager, destinationId) = SeedDestination(send: true);
            var handler = new ThrowingHandler();

            var channel = new SlackNotificationChannel(manager, new HttpClient(handler),
                maxRetryAttempts: 2, initialBackoff: TestBackoff, requestTimeout: TimeSpan.FromSeconds(5));

            int errors = 0;
            channel.ErrorHandled += _ => errors++;

            var ex = await Record.ExceptionAsync(() => channel.DeliverAsync(BuildMessage(destinationId, NotificationKind.Slack)));

            Assert.Null(ex);
            Assert.True(errors >= 1);
        }

        [Fact]
        public async Task FlushAsync_IsNoOp()
        {
            var manager = BuildManager();
            var channel = new SlackNotificationChannel(manager, new HttpClient(),
                maxRetryAttempts: 1, initialBackoff: TestBackoff, requestTimeout: TimeSpan.FromSeconds(5));

            var ex = await Record.ExceptionAsync(() => channel.FlushAsync());

            Assert.Null(ex);
        }


        private static SlackDestinationsManager BuildManager()
            => new(new Mock<IDatabaseCore>().Object);

        private static SlackDestination BuildDestination(bool send) =>
            new(new SlackDestinationEntity
            {
                Id = Guid.NewGuid().ToByteArray(),
                Author = Guid.NewGuid().ToByteArray(),
                CreationDate = DateTime.UtcNow.Ticks,
                Name = "alerts-channel",
                WebhookUrl = WebhookUrl,
                SendMessages = send,
            });

        private static (SlackDestinationsManager manager, Guid destinationId) SeedDestination(bool send)
        {
            var manager = BuildManager();
            var dest = BuildDestination(send);
            manager.TryAdd(dest.Id, dest);
            return (manager, dest.Id);
        }

        private static AlertMessage BuildMessage(Guid destinationId, NotificationKind kind)
        {
            var dest = new AlertDestination
            {
                Chats = new HashSet<Guid> { destinationId },
                AllChats = false,
                Kind = kind,
            };

            var alert = new AlertResult(dest, icon: "⚠️", comment: "value changed", policyId: Guid.NewGuid());

            return new AlertMessage(Guid.NewGuid(), new List<AlertResult> { alert });
        }


        private sealed class StubHandler : HttpMessageHandler
        {
            private readonly Queue<HttpStatusCode> _responses;

            public int Calls { get; private set; }
            public string LastRequestUri { get; private set; }

            internal StubHandler(params HttpStatusCode[] responses)
            {
                _responses = new Queue<HttpStatusCode>(responses);
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                Calls++;
                LastRequestUri = request.RequestUri?.ToString();

                var status = _responses.Count > 1 ? _responses.Dequeue() : (_responses.Count == 1 ? _responses.Peek() : HttpStatusCode.OK);

                return Task.FromResult(new HttpResponseMessage(status));
            }
        }

        private sealed class ThrowingHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
                => throw new InvalidOperationException("simulated network failure");
        }
    }
}
