using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.DataLayer;
using HSMServer.Migrations;
using HSMServer.Notifications.Chats;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.Notifications
{
    public class ChatMigrationTests
    {
        [Fact]
        public void Migrate_EmptyDb_WritesNothing()
        {
            var db = new Mock<IDatabaseCore>(MockBehavior.Strict);
            db.Setup(d => d.GetChats()).Returns(new List<ChatEntity>());
            db.Setup(d => d.GetTelegramChats()).Returns(new List<TelegramChatEntity>());
            db.Setup(d => d.GetSlackDestinations()).Returns(new List<SlackDestinationEntity>());

            new ChatMigrator().Migrate(db.Object);

            db.Verify(d => d.AddChat(It.IsAny<ChatEntity>()), Times.Never);
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

            new ChatMigrator().Migrate(db.Object);

            Assert.Equal(5, written.Count);

            var byId = written.ToDictionary(c => new Guid(c.Id));
            Assert.Equal(new[] { tg1Id, tg2Id, tg3Id, slack1Id, slack2Id }.OrderBy(g => g), byId.Keys.OrderBy(g => g));

            var migratedTg1 = byId[tg1Id];
            Assert.Equal("tg-1", migratedTg1.Name);
            Assert.Equal((byte)0, migratedTg1.TelegramType);
            Assert.Equal(100L, migratedTg1.TelegramChatId);
            Assert.NotNull(migratedTg1.AuthorizationTime);
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
        }

        [Fact]
        public void Migrate_AllLegacyIdsAlreadyPresent_IsNoop()
        {
            var tg1Id = Guid.NewGuid();
            var tg2Id = Guid.NewGuid();
            var slack1Id = Guid.NewGuid();

            var existing = new List<ChatEntity>
            {
                new() { Id = tg1Id.ToByteArray(), Name = "tg-1" },
                new() { Id = tg2Id.ToByteArray(), Name = "tg-2" },
                new() { Id = slack1Id.ToByteArray(), Name = "slack-1" },
            };

            var telegramChats = new List<TelegramChatEntity>
            {
                BuildTelegram(tg1Id, "tg-1", type: 0, chatId: 100L),
                BuildTelegram(tg2Id, "tg-2", type: 1, chatId: 200L),
            };
            var slackDestinations = new List<SlackDestinationEntity>
            {
                BuildSlack(slack1Id, "slack-1", "https://hooks.slack.com/services/A"),
            };

            var db = new Mock<IDatabaseCore>(MockBehavior.Strict);
            db.Setup(d => d.GetChats()).Returns(existing);
            db.Setup(d => d.GetTelegramChats()).Returns(telegramChats);
            db.Setup(d => d.GetSlackDestinations()).Returns(slackDestinations);

            new ChatMigrator().Migrate(db.Object);

            db.Verify(d => d.AddChat(It.IsAny<ChatEntity>()), Times.Never);
        }

        [Fact]
        public void Migrate_PartialRun_ResumesAndWritesOnlyMissingChats()
        {
            // Simulates a previously interrupted migration: tg1 already written, tg2/tg3/slack1/slack2 still pending.
            var tg1Id = Guid.NewGuid();
            var tg2Id = Guid.NewGuid();
            var tg3Id = Guid.NewGuid();
            var slack1Id = Guid.NewGuid();
            var slack2Id = Guid.NewGuid();

            var existing = new List<ChatEntity>
            {
                new() { Id = tg1Id.ToByteArray(), Name = "tg-1" },
            };

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
            db.Setup(d => d.GetChats()).Returns(existing);
            db.Setup(d => d.GetTelegramChats()).Returns(telegramChats);
            db.Setup(d => d.GetSlackDestinations()).Returns(slackDestinations);
            db.Setup(d => d.AddChat(It.IsAny<ChatEntity>())).Callback<ChatEntity>(written.Add);

            new ChatMigrator().Migrate(db.Object);

            Assert.Equal(4, written.Count);
            var writtenIds = written.Select(c => new Guid(c.Id)).ToHashSet();
            Assert.DoesNotContain(tg1Id, writtenIds);
            Assert.Contains(tg2Id, writtenIds);
            Assert.Contains(tg3Id, writtenIds);
            Assert.Contains(slack1Id, writtenIds);
            Assert.Contains(slack2Id, writtenIds);

            db.Verify(d => d.AddChat(It.IsAny<ChatEntity>()), Times.Exactly(4));
        }

        [Fact]
        public void Chat_DeserializedFromLegacyJson_HasNullTelegramTitleAndDescription()
        {
            // Real LevelDB read path: EnvironmentDatabaseWorker.GetChat calls
            // JsonSerializer.Deserialize<ChatEntity>. Legacy rows pre-#1283 lack the
            // TelegramChatTitle / TelegramChatDescription keys — System.Text.Json must
            // deserialize them as null (default behavior for missing properties).
            // Additive migration — no breaking schema change.
            var entity = new ChatEntity
            {
                Id = Guid.NewGuid().ToByteArray(),
                Author = Guid.NewGuid().ToByteArray(),
                CreationDate = DateTime.UtcNow.Ticks,
                Name = "On-call alerts",
                Description = "primary",
                SendMessages = true,
                MessagesAggregationTimeSec = 60,
            };

            var json = JsonNode.Parse(JsonSerializer.Serialize(entity)).AsObject();
            json.Remove(nameof(ChatEntity.TelegramChatTitle));
            json.Remove(nameof(ChatEntity.TelegramChatDescription));
            var legacyJson = json.ToJsonString();

            var deserialized = JsonSerializer.Deserialize<ChatEntity>(legacyJson);
            var chat = new Chat(deserialized);

            Assert.Null(chat.TelegramChatTitle);
            Assert.Null(chat.TelegramChatDescription);
            Assert.Equal("On-call alerts", chat.Name);
            Assert.Equal("primary", chat.Description);
        }

        [Fact]
        public void Chat_SyncUpdate_UpdatesTelegramFieldsAndLeavesNameAndDescriptionUntouched()
        {
            // Pins the contract TelegramBot.ChatNamesSynchronization relies on: the sync
            // update carries only TelegramChatTitle / TelegramChatDescription, so admin-set
            // Name / Description survive bot restarts. Mocking the Telegram.Bot client is
            // not worth the complexity for this contract pin.
            var chat = new Chat(new ChatEntity
            {
                Id = Guid.NewGuid().ToByteArray(),
                Author = Guid.NewGuid().ToByteArray(),
                CreationDate = DateTime.UtcNow.Ticks,
                Name = "On-call alerts",
                Description = "primary",
                SendMessages = true,
                MessagesAggregationTimeSec = 60,
            });

            chat.Update(new ChatUpdate
            {
                Id = chat.Id,
                TelegramChatTitle = "Actual Telegram Group",
                TelegramChatDescription = "Telegram-side description",
            });

            Assert.Equal("Actual Telegram Group", chat.TelegramChatTitle);
            Assert.Equal("Telegram-side description", chat.TelegramChatDescription);
            Assert.Equal("On-call alerts", chat.Name);
            Assert.Equal("primary", chat.Description);

            var roundTripped = new Chat(chat.ToEntity());
            Assert.Equal("Actual Telegram Group", roundTripped.TelegramChatTitle);
            Assert.Equal("Telegram-side description", roundTripped.TelegramChatDescription);
            Assert.Equal("On-call alerts", roundTripped.Name);
            Assert.Equal("primary", roundTripped.Description);
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
