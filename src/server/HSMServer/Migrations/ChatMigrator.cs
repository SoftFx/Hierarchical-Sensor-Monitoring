using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.DataLayer;
using NLog;
using System;
using System.Linq;

namespace HSMServer.Migrations
{
    public sealed class ChatMigrator
    {
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();


        public void Migrate(IDatabaseCore database)
        {
            if (database is null)
                throw new ArgumentNullException(nameof(database));

            if (database.GetChats().Count > 0)
            {
                _logger.Info("ChatMigrator: 'Chats' key already populated, skipping migration.");
                return;
            }

            var telegramChats = database.GetTelegramChats() ?? Enumerable.Empty<TelegramChatEntity>();
            var slackDestinations = database.GetSlackDestinations() ?? Enumerable.Empty<SlackDestinationEntity>();

            var migrated = 0;

            foreach (var tg in telegramChats)
            {
                database.AddChat(BuildFromTelegram(tg));
                migrated++;
            }

            foreach (var slack in slackDestinations)
            {
                database.AddChat(BuildFromSlack(slack));
                migrated++;
            }

            if (migrated == 0)
            {
                _logger.Info("ChatMigrator: no legacy chats found, nothing to migrate.");
                return;
            }

            database.RemoveTelegramChatsListKey();
            database.RemoveSlackDestinationsListKey();

            _logger.Info($"ChatMigrator: migrated {migrated} chats to the unified 'Chats' key.");
        }


        private static ChatEntity BuildFromTelegram(TelegramChatEntity tg) => new()
        {
            Id = tg.Id,
            Author = tg.Author,
            CreationDate = tg.CreationDate,
            Name = tg.Name,
            Description = tg.Description,

            SendMessages = tg.SendMessages,
            MessagesAggregationTimeSec = tg.MessagesAggregationTimeSec,

            TelegramType = tg.Type,
            TelegramChatId = tg.ChatId,
            AuthorizationTime = tg.AuthorizationTime,
        };

        private static ChatEntity BuildFromSlack(SlackDestinationEntity slack) => new()
        {
            Id = slack.Id,
            Author = slack.Author,
            CreationDate = slack.CreationDate,
            Name = slack.Name,
            Description = slack.Description,

            SendMessages = slack.SendMessages,
            MessagesAggregationTimeSec = slack.MessagesAggregationTimeSec,

            SlackWebhookUrl = slack.WebhookUrl,
        };
    }
}
