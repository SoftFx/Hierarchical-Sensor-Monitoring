using HSMServer.Model.Notifications;
using HSMServer.Notifications.Chats;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Linq;

namespace HSMServer.Extensions
{
    internal static class ChatSetupStepExtensions
    {
        /// <summary>
        /// A step of a chat setup guide as the web modal prints it: its parts in order, a code part
        /// in monospace, a link as a link. Every word is written as text, the bot username included.
        /// </summary>
        /// <param name="botName">
        /// The username of this server's bot, for the steps that name it. No username is the bot not
        /// being configured, a state the modal is shown in: the step then names an empty one, as it
        /// always did.
        /// </param>
        /// <param name="invitationLinks">
        /// The links behind the words that stand for an invitation. Those are never a setting: a
        /// step with such words shown without them is the caller's mistake.
        /// </param>
        /// <exception cref="InvalidOperationException">The step stands on an invitation and no links were given.</exception>
        public static IHtmlContent ToHtml(this ChatSetupStep step, string botName = null, ChatSetupInvitationLinks invitationLinks = null)
        {
            if (invitationLinks is null && step.Parts.Any(part => part is ChatSetupPart.StartCommand or ChatSetupPart.InvitationLink))
                throw new InvalidOperationException("The step stands on an invitation HSM issues, and the invitation needs its links.");

            var html = new HtmlContentBuilder();

            foreach (var part in step.Parts)
                switch (part)
                {
                    case ChatSetupPart.Plain text:
                        html.Append(text.Value);
                        break;

                    case ChatSetupPart.Code code:
                        html.AppendHtml("<code>").Append(code.Value).AppendHtml("</code>");
                        break;

                    case ChatSetupPart.Bold bold:
                        html.AppendHtml("<b>").Append(bold.Value).AppendHtml("</b>");
                        break;

                    case ChatSetupPart.Link link:
                        html.AppendHtml(Anchor(link.Address, link.Value, cssClass: null, opensTab: true));
                        break;

                    case ChatSetupPart.BotName:
                        html.Append(botName);
                        break;

                    case ChatSetupPart.StartCommand { Value: var command }:
                        html.AppendHtml(Invitation(command, invitationLinks, isDirectChatLink: false));
                        break;

                    case ChatSetupPart.InvitationLink { Value: var link }:
                        html.AppendHtml(Invitation(link, invitationLinks, isDirectChatLink: true));
                        break;

                    default:
                        throw new NotSupportedException($"A step of the chat setup guide has a part nothing shows: {part?.GetType().Name}");
                }

            return html;
        }


        // The words that stand for an invitation: a link inside a span. The span carries the tooltip
        // and the link the class, both the modal's. The invitation link of a direct chat opens a new
        // tab; the group command opens no page — the request behind its link puts the command in the
        // clipboard.
        private static TagBuilder Invitation(string text, ChatSetupInvitationLinks links, bool isDirectChatLink)
        {
            var span = new TagBuilder("span");
            span.Attributes["title"] = links.Title;
            span.InnerHtml.AppendHtml(Anchor(isDirectChatLink ? links.InvitationHref : links.GroupCommandHref, text, links.Class, isDirectChatLink));

            return span;
        }

        // An anchor without an address — none could be built for it — is no link.
        private static TagBuilder Anchor(string href, string text, string cssClass, bool opensTab)
        {
            var anchor = new TagBuilder("a");

            if (href is not null)
                anchor.Attributes["href"] = href;
            if (opensTab)
                anchor.Attributes["target"] = "_blank";
            if (cssClass is not null)
                anchor.Attributes["class"] = cssClass;

            anchor.InnerHtml.Append(text);

            return anchor;
        }
    }
}
