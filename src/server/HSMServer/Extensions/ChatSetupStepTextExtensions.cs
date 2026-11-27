using HSMServer.Notifications.Chats;
using System;
using System.Text;

namespace HSMServer.Extensions
{
    /// <summary>
    /// What the words that stand for an invitation read as in text — the twin of
    /// <see cref="Model.Notifications.ChatSetupInvitationLinks"/>. Either no invitation is issued
    /// yet and the words name the call that issues one (the guide), or one was just issued and
    /// the words carry it (the invitation's own answer).
    /// </summary>
    /// <param name="Link">How the invitation link of a direct chat reads, given the step's own words.</param>
    /// <param name="Command">How the /start command of a group reads.</param>
    internal sealed record ChatSetupInvitationText(Func<string, string> Link, string Command)
    {
        /// <summary>
        /// The guide: nothing is issued yet, so the words name the call that issues it, by kind
        /// ("direct" or "group").
        /// </summary>
        public static ChatSetupInvitationText NotIssued(Func<string, string> callOf) => new(
            _ => $"the `value` of `{callOf("direct")}`",
            $"the `value` of `{callOf("group")}`");

        /// <summary>The answer of a direct chat's invitation: the link under the step's own words.</summary>
        public static ChatSetupInvitationText IssuedLink(string link) => new(words => $"[{words}]({link})", Command: null);

        /// <summary>The answer of a group's invitation: the command as what the person posts.</summary>
        public static ChatSetupInvitationText IssuedCommand(string command) => new(Link: null, $"`{command}`");
    }


    /// <summary>
    /// A step of a chat setup guide as text — what an agent reads and relays (#1831), the twin of
    /// <see cref="ChatSetupStepExtensions.ToHtml"/>: the same parts, shown another way. A name to
    /// find or to type keeps its exact spelling in backticks, a documentation link is
    /// [words](url), a stressed word is the word, and the words that hold only in the web modal
    /// are left out.
    /// </summary>
    internal static class ChatSetupStepTextExtensions
    {
        /// <summary>
        /// What a step names the bot as when none is configured: the field that says so. The web
        /// modal names an empty one, as it always did.
        /// </summary>
        public const string NoBotName = "<botName>";

        /// <param name="step">The step to read.</param>
        /// <param name="botName">The username of this server's bot; null when none is stored.</param>
        /// <param name="invitation">What the words that stand for an invitation read as.</param>
        /// <exception cref="NotSupportedException">The step has a part nothing reads.</exception>
        public static string ToText(this ChatSetupStep step, string botName, ChatSetupInvitationText invitation)
        {
            var text = new StringBuilder();

            foreach (var part in step.Parts)
                switch (part)
                {
                    case ChatSetupPart.Plain { Value: var words }:
                        text.Append(words);
                        break;

                    case ChatSetupPart.Code { Value: var name }:
                        text.Append('`').Append(name).Append('`');
                        break;

                    case ChatSetupPart.Bold { Value: var word }:
                        text.Append(word);
                        break;

                    case ChatSetupPart.Link { Value: var words, Address: var address }:
                        text.Append('[').Append(words).Append("](").Append(address).Append(')');
                        break;

                    case ChatSetupPart.BotName:
                        text.Append(string.IsNullOrWhiteSpace(botName) ? NoBotName : botName);
                        break;

                    case ChatSetupPart.InvitationLink { Value: var words }:
                        text.Append(invitation.Link(words));
                        break;

                    case ChatSetupPart.StartCommand:
                        text.Append(invitation.Command);
                        break;

                    // True of the web modal only: there the part before says where the thing comes from.
                    case ChatSetupPart.InTheModal:
                        break;

                    default:
                        throw new NotSupportedException($"A step of the chat setup guide has a part nothing reads: {part?.GetType().Name}");
                }

            return text.ToString();
        }
    }
}
