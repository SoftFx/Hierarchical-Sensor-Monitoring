using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Notifications.Chats
{
    /// <summary>
    /// The name rule of a chat write (#1829) — ONE rule behind the web form and the
    /// management API, where each used to have its own half: the name is required, at
    /// most 100 characters, and unique per server ignoring case, a chat excluded from the
    /// check against itself. Alert import resolves chats by name, and the read DTO always
    /// promised the uniqueness that nothing enforced.
    /// </summary>
    internal static class ChatNameValidator
    {
        public const int NameMaxLength = 100;

        // The web form's messages (ChatViewModel's DataAnnotations) — the same texts. The
        // form formats its length message from the attribute's arguments, so a test keeps
        // the two equal rather than the compiler.
        public const string NameRequiredMessage = "Name is required";
        public static readonly string NameLengthMessage = $"Name length should be less than {NameMaxLength}.";
        public const string NameDuplicateMessage = "Chat with the same name already exists.";


        /// <summary>
        /// The message of the first rule the name breaks, or null when the name is valid.
        /// The name arrives trimmed (the write engine trims it). A row that KEEPS its own
        /// name breaks nothing: the rule guards changes, not history. Chats duplicated
        /// before the rule existed stay editable on both surfaces until someone renames
        /// them; a row whose name is longer than the rule allows (a Telegram group title,
        /// from before the /start flow cut them to the maximum) keeps it editable through
        /// the engine — the management API — while the web form's own length check asks
        /// for a shorter name first, as it always did.
        /// </summary>
        public static string FindError(string name, Guid chatId, IEnumerable<Chat> existing, string currentName = null)
        {
            if (string.IsNullOrEmpty(name))
                return NameRequiredMessage;

            if (string.Equals(name, currentName, StringComparison.Ordinal))
                return null;

            if (name.Length > NameMaxLength)
                return NameLengthMessage;

            return IsTaken(name, chatId, existing) ? NameDuplicateMessage : null;
        }

        /// <summary>Whether a chat OTHER than <paramref name="chatId"/> holds the name, ignoring case.</summary>
        public static bool IsTaken(string name, Guid chatId, IEnumerable<Chat> existing) =>
            existing.Any(chat => chat.Id != chatId && string.Equals(chat.Name, name, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// A name no stored chat holds yet: the proposed one, or it with " (2)", " (3)"…
        /// appended — for the rows the Telegram /start flow creates, named after the chat
        /// title or the username, where nobody is there to pick another name. A title
        /// longer than the rule allows (Telegram permits 128 characters) is cut to the
        /// maximum, so the flow never creates a row the web form cannot save; a blank
        /// proposal is left as it is.
        /// </summary>
        public static string UniqueIn(string proposed, IEnumerable<Chat> existing)
        {
            if (string.IsNullOrEmpty(proposed))
                return proposed;

            if (proposed.Length > NameMaxLength)
                proposed = proposed[..NameMaxLength];

            if (!IsTaken(proposed, Guid.Empty, existing))
                return proposed;

            for (var attempt = 2; ; attempt++)
            {
                var suffix = $" ({attempt})";
                var candidate = proposed[..Math.Min(proposed.Length, NameMaxLength - suffix.Length)] + suffix;

                if (!IsTaken(candidate, Guid.Empty, existing))
                    return candidate;
            }
        }
    }
}
