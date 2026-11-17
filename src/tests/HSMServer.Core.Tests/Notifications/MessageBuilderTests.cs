using System;
using System.Collections.Generic;
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

        [Fact]
        public void GetAggregateMessage_ThreeAlertsOneWithNullVariable_AreGroupedInOneLine()
        {
            var builder = new MessageBuilder();

            builder.AddMessage(BuildAlert(Template, comment: "limit is reached"));
            builder.AddMessage(BuildAlert(Template, comment: null));
            builder.AddMessage(BuildAlert(Template, comment: "no liquidity"));

            var message = builder.GetAggregateMessage();

            // The null lands at the start, in the middle or at the end of the item list,
            // depending on the dictionary order.
            var line = Assert.Single(message.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            Assert.StartsWith("[Product]Bridge/Errors [", line);
            Assert.Contains("limit is reached", line);
            Assert.Contains("no liquidity", line);
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

        // #1778. The drain reads the buffer branch by branch; the alert arrives when the drain is
        // past the branch it belongs to and has not finished yet.
        [Fact]
        public void AddMessage_ToBranchTheDrainHasAlreadyPassed_AlertIsInExactlyOneMessage()
        {
            var builder = new MessageBuilder();
            var groupedTemplates = new List<string>();

            void AddLateAlertOnSecondTemplate(string template)
            {
                groupedTemplates.Add(template);

                // Which template the drain reads first depends on the dictionary order: the late
                // alert goes to the one it has finished with.
                if (groupedTemplates.Count == 2)
                    builder.AddMessage(BuildAlert(groupedTemplates[0], comment: "late alert"));
            }

            foreach (var template in new[] { "one: $comment", "two: $comment" })
                builder.AddMessage(BuildAlert(template, comment: "on time", grouping: () => AddLateAlertOnSecondTemplate(template)));

            var messages = builder.GetAggregateMessage() + builder.GetAggregateMessage();

            Assert.Equal(2, groupedTemplates.Count); // the late alert was added, and during the first drain
            Assert.Equal(1, CountOf("late alert", messages));
            Assert.Equal(2, CountOf("on time", messages));
            Assert.Equal(string.Empty, builder.GetAggregateMessage());
        }


        private static int CountOf(string text, string message) => message.Split(text).Length - 1;

        private static AlertResult BuildAlert(string template, string comment, string rendered = null, Action grouping = null)
        {
            var state = new DrainInterleavingState
            {
                Template = AlertState.BuildSystemTemplate(template),
                Product = "Product",
                Path = "Bridge/Errors",
                Comment = comment,
                Grouping = grouping,
            };

            return new AlertResult(new AlertDestination(), string.Empty, rendered ?? state.BuildComment(), Guid.NewGuid(), template, state);
        }

        // Not produced by the server today; stands for any alert the grouping code fails on.
        private static AlertResult BuildAlertWithoutState(string comment) =>
            new(new AlertDestination(), string.Empty, comment, Guid.NewGuid(), Template, state: null);
    }
}
