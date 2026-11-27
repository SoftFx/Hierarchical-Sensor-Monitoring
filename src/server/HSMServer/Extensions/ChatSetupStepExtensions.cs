using HSMServer.Notifications.Chats;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Linq;

namespace HSMServer.Extensions
{
    /// <summary>
    /// The two links the Telegram setup modal puts behind the words of a step that stand for an
    /// invitation. HSM issues the invitation at the moment of use: the group command is fetched by
    /// the link's own request, the invitation link is the route of the action that opens the bot.
    /// Both carry the tooltip and the class as given — while the bot is not running, the reason and
    /// the greyed-out look.
    /// </summary>
    internal sealed record ChatSetupInvitationLinks(string Title, string Class, string GroupCommandHref, string InvitationHref);


    internal static class ChatSetupStepExtensions
    {
        /// <summary>
        /// A step of a chat setup guide as the web modal prints it: its parts in order, a code part
        /// in monospace, a link as a link. Every word is written as text, the bot username included.
        /// </summary>
        /// <param name="botName">The username of this server's bot, for the steps that name it.</param>
        /// <param name="invitationLinks">The links behind the words that stand for an invitation; a step with such words needs them.</param>
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
                        html.AppendHtml(Invitation(command, invitationLinks, invitationLinks.GroupCommandHref, opensTab: false));
                        break;

                    case ChatSetupPart.InvitationLink { Value: var link }:
                        html.AppendHtml(Invitation(link, invitationLinks, invitationLinks.InvitationHref, opensTab: true));
                        break;

                    default:
                        throw new NotSupportedException($"A step of the chat setup guide has a part nothing shows: {part?.GetType().Name}");
                }

            return html;
        }


        // The words that stand for an invitation: a link inside a span. The span carries the tooltip
        // and the link the class, both the modal's.
        private static TagBuilder Invitation(string text, ChatSetupInvitationLinks links, string href, bool opensTab)
        {
            var span = new TagBuilder("span");
            span.Attributes["title"] = links.Title;
            span.InnerHtml.AppendHtml(Anchor(href, text, links.Class, opensTab));

            return span;
        }

        // A group command opens no page: the request behind its link puts the command in the clipboard.
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
