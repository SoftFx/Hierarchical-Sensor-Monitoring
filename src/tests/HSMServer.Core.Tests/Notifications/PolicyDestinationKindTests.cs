using System;
using HSMDatabase.AccessManager.DatabaseEntities;
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
    }
}
