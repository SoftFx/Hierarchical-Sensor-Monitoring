using System;
using System.Collections.Generic;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.Notifications;
using Xunit;

namespace HSMServer.Core.Tests.Notifications
{
    public class PolicyDestinationKindTests
    {
        [Fact]
        public void Entity_without_kind_deserializes_to_Telegram()
        {
            var entity = new PolicyDestinationEntity
            {
                Chats = new() { { Guid.NewGuid().ToString(), "chat-1" } },
            };

            var destination = new PolicyDestination(entity);

            Assert.Equal(NotificationKind.Telegram, destination.Kind);
        }

        [Fact]
        public void Entity_with_empty_kind_deserializes_to_Telegram()
        {
            var entity = new PolicyDestinationEntity { Kind = string.Empty };

            var destination = new PolicyDestination(entity);

            Assert.Equal(NotificationKind.Telegram, destination.Kind);
        }

        [Fact]
        public void Entity_with_Slack_kind_deserializes_to_Slack()
        {
            var entity = new PolicyDestinationEntity { Kind = nameof(NotificationKind.Slack) };

            var destination = new PolicyDestination(entity);

            Assert.Equal(NotificationKind.Slack, destination.Kind);
        }

        [Fact]
        public void Kind_round_trips_through_entity()
        {
            var original = new PolicyDestination(new PolicyDestinationEntity { Kind = nameof(NotificationKind.Slack) });

            var roundTripped = new PolicyDestination(original.ToEntity());

            Assert.Equal(NotificationKind.Slack, roundTripped.Kind);
        }

        [Fact]
        public void Telegram_kind_round_trips_through_entity()
        {
            var original = new PolicyDestination(new PolicyDestinationEntity { Kind = nameof(NotificationKind.Telegram) });

            var roundTripped = new PolicyDestination(original.ToEntity());

            Assert.Equal(NotificationKind.Telegram, roundTripped.Kind);
        }

        [Fact]
        public void Update_with_kind_ctor_preserves_kind()
        {
            var chats = new Dictionary<Guid, string> { { Guid.NewGuid(), "slack-dest" } };

            var update = new PolicyDestinationUpdate(chats, PolicyDestinationMode.Custom, NotificationKind.Slack);

            Assert.Equal(NotificationKind.Slack, update.Kind);
            Assert.Equal(PolicyDestinationMode.Custom, update.Mode);
            Assert.Single(update.Chats);
        }

        [Fact]
        public void Update_default_ctor_leaves_kind_null()
        {
            var update = new PolicyDestinationUpdate(PolicyDestinationMode.Custom);

            Assert.Null(update.Kind);
        }
    }
}
