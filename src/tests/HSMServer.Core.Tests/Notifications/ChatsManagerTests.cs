using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Authentication;
using HSMServer.ConcurrentStorage;
using HSMServer.Core.DataLayer;
using HSMServer.Core.TableOfChanges;
using HSMServer.Notifications;
using HSMServer.Notifications.Chats;
using HSMServer.ServerConfiguration;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.Notifications
{
    public class ChatsManagerTests
    {
        [Fact]
        public async Task TryAdd_SlackOnlyChat_DoesNotPolluteTelegramChatIdsIndex()
        {
            var manager = BuildManager();
            var slackOnly = BuildSlackOnlyChat();

            var added = await manager.TryAdd(slackOnly);

            Assert.True(added);
            Assert.Single(manager.GetValues());
            Assert.Null(manager.GetChatByChatId(new Telegram.Bot.Types.ChatId(0L)));
        }

        [Fact]
        public async Task TryAdd_TelegramChat_RegistersInTelegramChatIdsIndex()
        {
            var manager = BuildManager();
            var telegramChat = BuildTelegramChat(chatId: 12345L);

            await manager.TryAdd(telegramChat);

            Assert.NotNull(manager.GetChatByChatId(new Telegram.Bot.Types.ChatId(12345L)));
        }

        // Regression: pre-#1265 TryAdd was `base.TryAdd(model) && (no telegram id || _telegramChatIds.TryAdd(...))`.
        // If base succeeded but the index TryAdd collided, the method returned false but the chat was
        // already in storage — a ghost chat that survives restart. The pre-flight check rejects before
        // any base mutation; this test pins the post-fix behavior (colliding add rejected + no ghost).
        [Fact]
        public async Task TryAdd_CollidingTelegramChatId_RejectedAndNotAddedToBase()
        {
            const long sharedChatId = 999L;
            var manager = BuildManager();
            var first = BuildTelegramChat(sharedChatId);
            var firstAdded = await manager.TryAdd(first);
            Assert.True(firstAdded);

            var colliding = new Chat(new Telegram.Bot.Types.ChatId(sharedChatId))
            {
                TelegramType = ConnectedChatType.TelegramPrivate,
            };
            var collidingAdded = await manager.TryAdd(colliding);

            Assert.False(collidingAdded);
            Assert.Single(manager.GetValues());
            Assert.Same(first, manager.GetChatByChatId(new Telegram.Bot.Types.ChatId(sharedChatId)));
        }

        [Fact]
        public async Task TryRemove_SlackOnlyChat_DoesNotThrowOnMissingTelegramIndex()
        {
            var manager = BuildManager();
            var slackOnly = BuildSlackOnlyChat();
            await manager.TryAdd(slackOnly);

            var removed = await manager.TryRemove(new RemoveRequest(slackOnly.Id, InitiatorInfo.System));

            Assert.True(removed);
            Assert.Empty(manager.GetValues());
        }

        [Fact]
        public async Task TryRemove_TelegramChat_RemovesFromTelegramChatIdsIndex()
        {
            const long knownChatId = 42L;
            var manager = BuildManager();
            var chat = BuildTelegramChat(knownChatId);
            await manager.TryAdd(chat);

            Assert.NotNull(manager.GetChatByChatId(new Telegram.Bot.Types.ChatId(knownChatId)));

            var removed = await manager.TryRemove(new RemoveRequest(chat.Id, InitiatorInfo.System));

            Assert.True(removed);
            Assert.Null(manager.GetChatByChatId(new Telegram.Bot.Types.ChatId(knownChatId)));
        }

        [Fact]
        public async Task TryUpdate_TelegramChatIdChange_RekeysTelegramChatIdsIndex()
        {
            const long oldChatId = 100L;
            const long newChatId = 200L;
            var manager = BuildManager();
            var chat = BuildTelegramChat(oldChatId);
            await manager.TryAdd(chat);

            var updated = await manager.TryUpdate(new ChatUpdate
            {
                Id = chat.Id,
                TelegramChatId = newChatId,
            });

            Assert.True(updated);
            Assert.Null(manager.GetChatByChatId(new Telegram.Bot.Types.ChatId(oldChatId)));
            Assert.Same(chat, manager.GetChatByChatId(new Telegram.Bot.Types.ChatId(newChatId)));
        }

        [Fact]
        public async Task MigrateToSupergroup_RekeysTelegramChatIdsIndex()
        {
            const long oldChatId = 100L;
            const long newChatId = 200L;
            var manager = BuildManager();
            var chat = BuildTelegramChat(oldChatId);
            await manager.TryAdd(chat);

            await manager.MigrateToSupergroup(oldChatId, newChatId);

            Assert.Null(manager.GetChatByChatId(new Telegram.Bot.Types.ChatId(oldChatId)));
            Assert.Same(chat, manager.GetChatByChatId(new Telegram.Bot.Types.ChatId(newChatId)));
        }

        [Fact]
        public void Chat_BuiltFromPublicCtor_PersistsTelegramFieldsThroughToEntity()
        {
            // Ctor must default TelegramType — ToEntity() gates Telegram persistence on it.
            var chat = new Chat(new Telegram.Bot.Types.ChatId(42L));

            var entity = chat.ToEntity();

            Assert.Equal((byte)ConnectedChatType.TelegramPrivate, entity.TelegramType);
            Assert.Equal(42L, entity.TelegramChatId);

            var reloaded = new Chat(entity);
            Assert.Equal(42L, reloaded.TelegramChatId?.Identifier);
            Assert.Equal(ConnectedChatType.TelegramPrivate, reloaded.TelegramType);
        }


        private static ChatsManager BuildManager()
        {
            var db = new Mock<IDatabaseCore>(MockBehavior.Loose);
            db.Setup(d => d.GetChats()).Returns(new List<ChatEntity>());

            var users = new Mock<IUserManager>(MockBehavior.Loose);
            var config = new Mock<IServerConfig>(MockBehavior.Loose);
            config.SetupGet(c => c.Telegram).Returns(new TelegramConfig { BotName = "test_bot", BotToken = "test_token" });

            return new ChatsManager(db.Object, users.Object, config.Object);
        }

        private static Chat BuildSlackOnlyChat() => new()
        {
            // Slack-only chat: no TelegramChatId/TelegramType set
        };

        private static Chat BuildTelegramChat(long chatId) => new(new Telegram.Bot.Types.ChatId(chatId))
        {
            TelegramType = ConnectedChatType.TelegramPrivate,
        };
    }
}

