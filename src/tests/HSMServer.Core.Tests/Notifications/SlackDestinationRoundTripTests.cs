using System;
using HSMServer.Notifications;
using Xunit;

namespace HSMServer.Core.Tests.Notifications
{
    public class SlackDestinationRoundTripTests
    {
        [Fact]
        public void ToEntity_MapsModelFieldsToEntity()
        {
            var destination = new SlackDestination(new SlackAddRequest
            {
                AuthorId = Guid.NewGuid(),
                Name = "alerts",
                WebhookUrl = "https://hooks.slack.com/services/X",
            });

            var entity = destination.ToEntity();

            Assert.Equal("alerts", entity.Name);
            Assert.Equal("https://hooks.slack.com/services/X", entity.WebhookUrl);
            Assert.True(entity.SendMessages);
            Assert.Equal(destination.Id, new Guid(entity.Id));
        }

        [Fact]
        public void NewDestination_DefaultsSendMessagesTrue()
        {
            var destination = new SlackDestination(new SlackAddRequest
            {
                AuthorId = Guid.NewGuid(),
                Name = "alerts",
                WebhookUrl = "https://hooks.slack.com/services/X",
            });

            Assert.True(destination.SendMessages);
        }
    }
}
