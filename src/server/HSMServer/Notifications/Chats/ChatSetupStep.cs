using System;
using System.Collections.Generic;

namespace HSMServer.Notifications.Chats
{
    /// <summary>
    /// Where a step of a chat setup guide is carried out. A step is the messenger's while the person
    /// acts inside Slack, Mattermost or Telegram — there an agent can only relay the instruction.
    /// A step is HSM's once the person acts in HSM's own form. The web modal prints both; the guide an
    /// agent reads through the management API (#1831) answers the messenger's steps and names, for an
    /// HSM step, the tool call that does the same.
    /// </summary>
    internal enum ChatSetupSide : byte
    {
        Messenger,

        Hsm,
    }


    /// <summary>
    /// One inline piece of a step's text. A step reads as its parts in order; what a part is decides
    /// how it is shown.
    /// </summary>
    internal abstract record ChatSetupPart
    {
        private ChatSetupPart() { }

        /// <summary>The words as they are written.</summary>
        public sealed record Plain(string Value) : ChatSetupPart;

        /// <summary>Something to find or to type spelled exactly this way: a menu item, a button, an address.</summary>
        public sealed record Code(string Value) : ChatSetupPart;

        /// <summary>A word the step stresses.</summary>
        public sealed record Bold(string Value) : ChatSetupPart;

        /// <summary>Words that open a page of the messenger's documentation.</summary>
        public sealed record Link(string Value, string Address) : ChatSetupPart;

        /// <summary>The username of this server's bot, as the one showing the step knows it.</summary>
        public sealed record BotName : ChatSetupPart;

        /// <summary>
        /// Words that stand for the /start command HSM issues to bind a group chat. The web modal
        /// copies the command to the clipboard when they are clicked.
        /// </summary>
        public sealed record StartCommand(string Value) : ChatSetupPart;

        /// <summary>Words that stand for the invitation link HSM issues to bind a direct chat.</summary>
        public sealed record InvitationLink(string Value) : ChatSetupPart;


        // A string on its own is plain words: the guide writes a step as what it says, with the
        // other parts between the words.
        public static implicit operator ChatSetupPart(string value) => new Plain(value);
    }


    /// <summary>
    /// One step of a chat setup guide: where it is carried out, and what it says as parts in order.
    /// </summary>
    internal sealed class ChatSetupStep
    {
        public ChatSetupStep(ChatSetupSide side, params ChatSetupPart[] parts)
        {
            Side = side;
            Parts = parts;
        }

        public ChatSetupSide Side { get; }

        public IReadOnlyList<ChatSetupPart> Parts { get; }
    }
}
