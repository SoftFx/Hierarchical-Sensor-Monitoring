using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Notifications.Chats
{
    /// <summary>
    /// The name rule of a chat write (#1829) — ONE rule behind the web form and the
    /// management API, where each used to have its own half: the name is required, at
    /// most 100 characters, and unique per server ignoring case and surrounding spaces, a
    /// chat excluded from the check against itself. Alert import resolves chats by name,
    /// and the read DTO always promised the uniqueness that nothing enforced.
    /// The engine runs the rule on a snapshot; the storage repeats the uniqueness part in
    /// front of its own write (<see cref="ChatsManager.TryAdd(Chat)"/>), which is what
    /// makes it hold under two writes at once.
    /// </summary>
    internal static class ChatNameValidator
    {
        public const int NameMaxLength = 100;

        // The web form's messages (ChatViewModel's DataAnnotations) — the same texts. The
        // form formats its length message from the attribute's arguments, so a test keeps
        // the two equal rather than the compiler.
        public const string NameRequiredMessage = "Name is required";
        public static readonly string NameLengthMessage = $"Name length should be at most {NameMaxLength} characters.";
        public const string NameDuplicateMessage = "Chat with the same name already exists.";


        /// <summary>
        /// The message of the first rule the name breaks, or null when the name is valid.
        /// The name arrives trimmed (the write engine trims it); a stored name is compared
        /// trimmed as well, so a row stored with surrounding spaces, from before the name
        /// was held trimmed, holds the same name it reads as. A row that KEEPS its own
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

            // The posted name arrives trimmed — or comes back as the row's own stored one,
            // kept as it is by the engine. Either way, surrounding spaces make no difference.
            if (SameName(name, currentName))
                return null;

            if (name.Length > NameMaxLength)
                return NameLengthMessage;

            return IsTaken(name, chatId, existing) ? NameDuplicateMessage : null;
        }

        /// <summary>
        /// Whether two names are the same one: equal once trimmed, letter case included —
        /// the same name in another case is another name.
        /// </summary>
        public static bool SameName(string left, string right) =>
            string.Equals(left?.Trim(), right?.Trim(), StringComparison.Ordinal);

        /// <summary>
        /// Whether a chat OTHER than <paramref name="chatId"/> holds the name, ignoring
        /// case and surrounding spaces. A blank name is held by no one: rows without a
        /// name (a Telegram user that has no username) are not each other's duplicates.
        /// </summary>
        public static bool IsTaken(string name, Guid chatId, IEnumerable<Chat> existing) =>
            !string.IsNullOrEmpty(name)
            && existing.Any(chat => chat.Id != chatId && string.Equals(chat.Name?.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Whether a write renames a chat onto a name another chat holds — the one thing
        /// the rule forbids. The engine answers it against a snapshot; the storage repeats
        /// it under its write gate and has the last word. A name the chat already holds
        /// (<paramref name="storedName"/>) is no rename, and neither is no name.
        /// </summary>
        public static bool RenamesOntoATakenName(string name, Guid chatId, IEnumerable<Chat> existing, string storedName) =>
            name is not null && !SameName(name, storedName) && IsTaken(name, chatId, existing);

        /// <summary>
        /// A name no stored chat holds yet: the proposed one trimmed, or it with " (2)",
        /// " (3)"… appended — for the rows the Telegram /start flow creates, named after
        /// the chat title or the username, where nobody is there to pick another name. A
        /// title longer than the rule allows (Telegram permits 128 characters) is cut to
        /// the maximum, so the name never breaks the length rule. A blank proposal — a
        /// Telegram user without a username — is left as it is: the row then has no name
        /// until one is given to it in the web form, which asks for it.
        /// </summary>
        public static string UniqueIn(string proposed, IEnumerable<Chat> existing)
        {
            proposed = proposed?.Trim();

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
