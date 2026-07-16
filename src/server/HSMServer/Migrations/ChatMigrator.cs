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

            // Post-#1261 state: the unified `Chats` LevelDB key is authoritative. All readers and
            // writers go through IChatsManager; the legacy TelegramChatsManager / SlackDestinationsManager
            // and their write paths are gone.
            //
            // This migration is intentionally additive and idempotent:
            //   - First boot on a pre-#1260 installation: every legacy TelegramChatEntity /
            //     SlackDestinationEntity gets a unified twin written under `Chats`.
            //   - Subsequent boots: every legacy id is already in `existingIds`, so the loops are
            //     a no-op and `AddChat` is never called.
            //   - The legacy read paths (GetTelegramChats / GetSlackDestinations on IDatabaseCore)
            //     are kept solely for this migrator. They are read-only — no service writes to the
            //     legacy keys anymore, so legacy data is frozen as of the first #1260 boot.
            //   - Removal of the legacy `TelegramChats` / `SlackDestinations` LevelDB key families
            //     is deferred to a later cleanup PR; until then they cost disk space but nothing
            //     reads them outside this migrator.
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
