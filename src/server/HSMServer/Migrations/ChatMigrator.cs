using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.DataLayer;
using NLog;
using System;
using System.Collections.Generic;
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

            var existing = database.GetChats() ?? new List<ChatEntity>();
            var existingIds = new HashSet<Guid>(existing.Select(c => new Guid(c.Id)));

            var telegramChats = database.GetTelegramChats() ?? Enumerable.Empty<TelegramChatEntity>();
            var slackDestinations = database.GetSlackDestinations() ?? Enumerable.Empty<SlackDestinationEntity>();

            var written = 0;
            var skipped = 0;

            foreach (var tg in telegramChats)
            {
                if (existingIds.Contains(new Guid(tg.Id)))
                {
                    skipped++;
                    continue;
                }

                database.AddChat(BuildFromTelegram(tg));
                written++;
            }

            foreach (var slack in slackDestinations)
            {
                if (existingIds.Contains(new Guid(slack.Id)))
                {
                    skipped++;
                    continue;
                }

                database.AddChat(BuildFromSlack(slack));
                written++;
            }

            _logger.Info($"ChatMigrator: wrote {written} chats, skipped {skipped} already-present entries.");

            // This migration is intentionally additive and is not the source of truth yet.
            //   - Legacy `TelegramChats` / `SlackDestinations` keys are left intact; their managers
            //     remain active and consumable until #1261 switches readers to IChatsManager.
            //   - Renames / field changes / deletes performed through the legacy UI *after* this
            //     migration runs are NOT propagated: the migrator skips by id and never updates or
            //     deletes. #1261 must reconcile updates and deletes (not just adds) before it can
            //     treat the unified `Chats` key as authoritative.
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
