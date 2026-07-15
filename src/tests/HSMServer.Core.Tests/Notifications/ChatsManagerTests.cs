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

