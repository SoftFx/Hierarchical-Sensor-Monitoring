using System;
using System.Collections.Generic;
using System.Linq;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.DataLayer;
using HSMServer.Migrations;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.Notifications
{
    public class ChatMigrationTests
    {
        [Fact]
        public void Migrate_EmptyDb_WritesNothingAndDoesNotDeleteKeys()
        {
            var db = new Mock<IDatabaseCore>(MockBehavior.Strict);
            db.Setup(d => d.GetChats()).Returns(new List<ChatEntity>());
            db.Setup(d => d.GetTelegramChats()).Returns(new List<TelegramChatEntity>());
            db.Setup(d => d.GetSlackDestinations()).Returns(new List<SlackDestinationEntity>());

            new ChatMigrator().Migrate(db.Object);

            db.Verify(d => d.AddChat(It.IsAny<ChatEntity>()), Times.Never);
            db.Verify(d => d.RemoveTelegramChatsListKey(), Times.Never);
            db.Verify(d => d.RemoveSlackDestinationsListKey(), Times.Never);
        }

        [Fact]
        public void Migrate_ThreeTelegramAndTwoSlack_ProducesFiveChatsWithCorrectFields()
        {
            var tg1Id = Guid.NewGuid();
            var tg2Id = Guid.NewGuid();
            var tg3Id = Guid.NewGuid();
            var slack1Id = Guid.NewGuid();
            var slack2Id = Guid.NewGuid();

            var telegramChats = new List<TelegramChatEntity>
            {
                BuildTelegram(tg1Id, "tg-1", type: 0, chatId: 100L),
                BuildTelegram(tg2Id, "tg-2", type: 1, chatId: 200L),
                BuildTelegram(tg3Id, "tg-3", type: 0, chatId: 300L),
            };
            var slackDestinations = new List<SlackDestinationEntity>
            {
                BuildSlack(slack1Id, "slack-1", "https://hooks.slack.com/services/A"),
                BuildSlack(slack2Id, "slack-2", "https://hooks.slack.com/services/B"),
            };

            var written = new List<ChatEntity>();
            var db = new Mock<IDatabaseCore>(MockBehavior.Strict);
            db.Setup(d => d.GetChats()).Returns(new List<ChatEntity>());
            db.Setup(d => d.GetTelegramChats()).Returns(telegramChats);
            db.Setup(d => d.GetSlackDestinations()).Returns(slackDestinations);
            db.Setup(d => d.AddChat(It.IsAny<ChatEntity>())).Callback<ChatEntity>(written.Add);
            db.Setup(d => d.RemoveTelegramChatsListKey());
            db.Setup(d => d.RemoveSlackDestinationsListKey());

            new ChatMigrator().Migrate(db.Object);

            Assert.Equal(5, written.Count);

            var byId = written.ToDictionary(c => new Guid(c.Id));
            Assert.Equal(new[] { tg1Id, tg2Id, tg3Id, slack1Id, slack2Id }.OrderBy(g => g), byId.Keys.OrderBy(g => g));

            var migratedTg1 = byId[tg1Id];
            Assert.Equal("tg-1", migratedTg1.Name);
            Assert.Equal((byte)0, migratedTg1.TelegramType);
            Assert.Equal(100L, migratedTg1.TelegramChatId);
            Assert.Null(migratedTg1.SlackWebhookUrl);
            Assert.Null(migratedTg1.MattermostWebhookUrl);

            var migratedTg2 = byId[tg2Id];
            Assert.Equal((byte)1, migratedTg2.TelegramType);
            Assert.Equal(200L, migratedTg2.TelegramChatId);

            var migratedSlack1 = byId[slack1Id];
            Assert.Equal("slack-1", migratedSlack1.Name);
            Assert.Equal("https://hooks.slack.com/services/A", migratedSlack1.SlackWebhookUrl);
            Assert.Null(migratedSlack1.TelegramType);
            Assert.Null(migratedSlack1.TelegramChatId);
            Assert.Null(migratedSlack1.AuthorizationTime);

            db.Verify(d => d.AddChat(It.IsAny<ChatEntity>()), Times.Exactly(5));
            db.Verify(d => d.RemoveTelegramChatsListKey(), Times.Once);
            db.Verify(d => d.RemoveSlackDestinationsListKey(), Times.Once);
        }

        [Fact]
        public void Migrate_AlreadyMigrated_IsNoop()
        {
            var existing = new List<ChatEntity>
            {
                new() { Id = Guid.NewGuid().ToByteArray(), Name = "chat-1" },
                new() { Id = Guid.NewGuid().ToByteArray(), Name = "chat-2" },
            };

            var db = new Mock<IDatabaseCore>(MockBehavior.Strict);
            db.Setup(d => d.GetChats()).Returns(existing);

            new ChatMigrator().Migrate(db.Object);

            db.Verify(d => d.GetTelegramChats(), Times.Never);
            db.Verify(d => d.GetSlackDestinations(), Times.Never);
            db.Verify(d => d.AddChat(It.IsAny<ChatEntity>()), Times.Never);
            db.Verify(d => d.RemoveTelegramChatsListKey(), Times.Never);
            db.Verify(d => d.RemoveSlackDestinationsListKey(), Times.Never);
        }


        private static TelegramChatEntity BuildTelegram(Guid id, string name, byte type, long chatId) => new()
        {
            Id = id.ToByteArray(),
            Author = Guid.NewGuid().ToByteArray(),
            CreationDate = DateTime.UtcNow.Ticks,
            Name = name,
            Description = $"{name}-desc",
            Type = type,
            ChatId = chatId,
            AuthorizationTime = DateTime.UtcNow.Ticks,
            SendMessages = true,
            MessagesAggregationTimeSec = 60,
        };

        private static SlackDestinationEntity BuildSlack(Guid id, string name, string webhook) => new()
        {
            Id = id.ToByteArray(),
            Author = Guid.NewGuid().ToByteArray(),
            CreationDate = DateTime.UtcNow.Ticks,
            Name = name,
            Description = $"{name}-desc",
            WebhookUrl = webhook,
            SendMessages = false,
            MessagesAggregationTimeSec = 30,
        };
    }
}
