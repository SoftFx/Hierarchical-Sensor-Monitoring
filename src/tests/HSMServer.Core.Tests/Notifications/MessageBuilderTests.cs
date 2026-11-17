using System;
using HSMServer.Core.Model.Policies;
using HSMServer.Notifications.AddressBook;
using Xunit;

namespace HSMServer.Core.Tests.Notifications
{
    // The aggregated message of a chat is built once per aggregation period from the alerts
    // buffered since the previous one. A failure while building it must never leave the buffer
    // behind: the next period would hit the same alert again and the chat would stay silent
    // until the server restarts (#1776: one production chat was silent for four days).
    public class MessageBuilderTests
    {
        private const string Template = "[$product]$path $comment";


        [Fact]
        public void GetAggregateMessage_AlertsDifferOnlyInVariableThatIsNullInOne_AreGroupedInOneLine()
        {
            var builder = new MessageBuilder();

            builder.AddMessage(BuildAlert(Template, comment: null));
            builder.AddMessage(BuildAlert(Template, comment: "limit is reached"));

            var message = builder.GetAggregateMessage();

            // Which of the two alerts starts the group depends on the dictionary order.
            var line = Assert.Single(message.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            Assert.Matches(@"^\[Product\]Bridge/Errors \[(, limit is reached|limit is reached, )\]$", line);
        }

        // Record.Exception: the next message must be empty whether the first drain threw or not.
        [Fact]
        public void GetAggregateMessage_AlertsDifferOnlyInVariableThatIsNullInOne_NextMessageIsEmpty()
        {
            var builder = new MessageBuilder();

            builder.AddMessage(BuildAlert(Template, comment: null));
            builder.AddMessage(BuildAlert(Template, comment: "limit is reached"));

            Record.Exception(builder.GetAggregateMessage);

            Assert.Equal(string.Empty, builder.GetAggregateMessage());
        }

        [Fact]
        public void GetAggregateMessage_OneAlertCannotBeGrouped_OtherAlertsAreStillDelivered()
        {
            var builder = new MessageBuilder();

            builder.AddMessage(BuildAlert(Template, comment: "healthy alert"));
            builder.AddMessage(BuildAlertWithoutState("alert without a state"));

            var message = builder.GetAggregateMessage();

            Assert.Contains("healthy alert", message);
            Assert.Contains("alert without a state", message);
        }

        [Fact]
        public void GetAggregateMessage_OneAlertCannotBeGrouped_IsNotRepeatedInNextMessage()
        {
            var builder = new MessageBuilder();

            builder.AddMessage(BuildAlert(Template, comment: "healthy alert"));
            builder.AddMessage(BuildAlertWithoutState("alert without a state"));

            Record.Exception(builder.GetAggregateMessage);

            Assert.Equal(string.Empty, builder.GetAggregateMessage());
        }

        [Fact]
        public void GetAggregateMessage_GroupCannotBeRendered_ItsFirstAlertIsDeliveredAsItIs()
        {
            // The two alerts are grouped by $comment; rendering the group formats the template
            // again and fails on the unbalanced brace.
            const string brokenTemplate = "$comment {broken";
            var builder = new MessageBuilder();

            builder.AddMessage(BuildAlert(brokenTemplate, comment: "first", rendered: "first rendered"));
            builder.AddMessage(BuildAlert(brokenTemplate, comment: "second", rendered: "second rendered"));

            var message = builder.GetAggregateMessage();

            Assert.Matches("^(first|second) rendered\\s*$", message);
            Assert.Equal(string.Empty, builder.GetAggregateMessage());
        }


        private static AlertResult BuildAlert(string template, string comment, string rendered = null)
        {
            var state = new AlertState
            {
                Template = AlertState.BuildSystemTemplate(template),
                Product = "Product",
                Path = "Bridge/Errors",
                Comment = comment,
            };

            return new AlertResult(new AlertDestination(), string.Empty, rendered ?? state.BuildComment(), Guid.NewGuid(), template, state);
        }

        // Not produced by the server today; stands for any alert the grouping code fails on.
        private static AlertResult BuildAlertWithoutState(string comment) =>
            new(new AlertDestination(), string.Empty, comment, Guid.NewGuid(), Template, state: null);
    }
}
