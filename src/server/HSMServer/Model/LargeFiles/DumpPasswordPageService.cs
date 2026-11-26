using HSMServer.Model.TreeViewModel;
using System;
using System.Collections.Generic;
using System.Linq;
using HSMServer.Core.Cache;
using HSMServer.Core.LargeFiles.DumpPasswords;
using HSMServer.Core.TableOfChanges;
using HSMServer.Model.Authentication;
using HSMServer.ServerConfiguration;

namespace HSMServer.Model.LargeFiles
{
    /// <summary>One kept version of a dump password as the settings page lists it: never the value.</summary>
    public sealed record DumpPasswordVersionViewModel(int Version, DateTime SetUtc, string SetBy, bool IsCurrent, DateTime? RetiredUtc, DateTime? KeptUntilUtc);


    /// <summary>The dump password section of a product's settings page. Values are never part of it.</summary>
    public sealed record DumpPasswordViewModel
    {
        /// <summary>The root product the password belongs to.</summary>
        public Guid ProductId { get; init; }

        public bool HasPassword { get; init; }

        /// <summary>The stored passwords of the product cannot be read: unavailable, not unset, and no new one is saved.</summary>
        public bool IsUnreadable { get; init; }

        /// <summary>A Manager of the product or an admin: sees the versions and may show, generate and set.</summary>
        public bool CanManage { get; init; }

        /// <summary>Newest first; empty for a caller who cannot manage.</summary>
        public IReadOnlyList<DumpPasswordVersionViewModel> Versions { get; init; } = [];

        public int MinTypedLength => DumpPasswordRules.MinTypedLength;

        public int MaxLength => DumpPasswordRules.MaxLength;
    }


    /// <summary>One kept version WITH its value: only in the JSON answer of the "show" action, never in a page.</summary>
    public sealed record DumpPasswordValueDto(int Version, string Password, DateTime SetUtc, string SetBy, bool IsCurrent, DateTime? RetiredUtc, DateTime? KeptUntilUtc);


    public enum DumpPasswordPageOutcome
    {
        Ok,
        NotFound,
        Forbidden,
        Invalid,
    }


    public sealed record DumpPasswordPageResult<T>(DumpPasswordPageOutcome Outcome, T Value = default, string Message = null)
    {
        public static DumpPasswordPageResult<T> Fail(DumpPasswordPageOutcome outcome, string message = null) => new(outcome, default, message);
    }


    /// <summary>
    /// The decisions of the dump password section of a product's settings (ADR-0022 point 13, #1885): the state for
    /// everyone who opens the page, the versions and their values for a Manager of the product or an admin. The password
    /// belongs to the ROOT product: a nested node resolves to it. Nothing here logs or returns a value except
    /// <see cref="Show"/> and <see cref="Generate"/>, whose answers go only into the JSON of their POST.
    /// </summary>
    public sealed class DumpPasswordPageService
    {
        internal const string SavedMessage = "Saved: dump password version {0}. The kept secure archives of the product are re-encrypted with it in the background.";

        internal const string UnreadableMessage =
            "The stored dump passwords of this product cannot be read, so a new password is not saved over them. " +
            "An administrator must look at the configuration database.";

        internal static readonly string TooLongMessage = $"The password is too long: at most {DumpPasswordRules.MaxLength} characters.";

        private readonly ITreeValuesCache _cache;
        private readonly IDumpPasswordService _passwords;
        private readonly TimeSpan _retention;


        public DumpPasswordPageService(ITreeValuesCache cache, IDumpPasswordService passwords, IServerConfig config)
        {
            _cache = cache;
            _passwords = passwords;
            _retention = (config?.LargeFileArchives ?? new LargeFileArchivesConfig()).PasswordRetention;
        }


        /// <summary>The section of the page; null when the product is unknown or the user may not see it.</summary>
        public DumpPasswordViewModel GetSection(User user, Guid productId)
        {
            if (!TryGetRoot(productId, out var rootId) || !UserTreeAccess.IsRootProductVisibleTo(user, rootId))
                return null;

            var canManage = UserTreeAccess.CanManageRootProduct(user, rootId);
            var current = canManage ? _passwords.GetCurrent(rootId)?.Version : null;

            return new DumpPasswordViewModel
            {
                ProductId = rootId,
                HasPassword = _passwords.HasPassword(rootId),
                IsUnreadable = _passwords.IsUnreadable(rootId),
                CanManage = canManage,
                Versions = canManage
                    ? ListVersions(rootId, version => new DumpPasswordVersionViewModel(version.Version, version.SetUtc, version.SetBy,
                        version.Version == current, version.RetiredUtc, KeptUntil(version)))
                    : [],
            };
        }

        /// <summary>Every kept version with its value, newest first.</summary>
        public DumpPasswordPageResult<IReadOnlyList<DumpPasswordValueDto>> Show(User user, Guid productId)
        {
            if (!TryAuthorize(user, productId, out var rootId, out var failure))
                return DumpPasswordPageResult<IReadOnlyList<DumpPasswordValueDto>>.Fail(failure);

            var current = _passwords.GetCurrent(rootId)?.Version;

            return new(DumpPasswordPageOutcome.Ok, ListVersions(rootId, version =>
                new DumpPasswordValueDto(version.Version, version.Password, version.SetUtc, version.SetBy, version.Version == current,
                    version.RetiredUtc, KeptUntil(version))));
        }

        /// <summary>A new password from the server's generator; nothing is stored until it is saved.</summary>
        public DumpPasswordPageResult<string> Generate(User user, Guid productId) =>
            TryAuthorize(user, productId, out _, out var failure)
                ? new(DumpPasswordPageOutcome.Ok, _passwords.Generate())
                : DumpPasswordPageResult<string>.Fail(failure);

        /// <summary>
        /// Stores a new version. A password the page generated may be sent as generated; anything else is checked as typed
        /// (a "generated" password that does not look like one is checked as typed too, so the flag cannot skip the rules).
        /// </summary>
        public DumpPasswordPageResult<int> Set(User user, Guid productId, string password, bool generated, InitiatorInfo initiator)
        {
            if (!TryAuthorize(user, productId, out var rootId, out var failure))
                return DumpPasswordPageResult<int>.Fail(failure);

            password ??= string.Empty;

            if (password.Length > DumpPasswordRules.MaxLength)
                return DumpPasswordPageResult<int>.Fail(DumpPasswordPageOutcome.Invalid, TooLongMessage);

            generated = generated && LooksGenerated(password);

            var result = _passwords.SetPassword(rootId, password, generated, initiator);

            return result.Outcome switch
            {
                DumpPasswordSetOutcome.Set => new(DumpPasswordPageOutcome.Ok, result.Version, string.Format(SavedMessage, result.Version)),
                DumpPasswordSetOutcome.TooShort => DumpPasswordPageResult<int>.Fail(DumpPasswordPageOutcome.Invalid,
                    $"The password is too short: a typed password needs at least {DumpPasswordRules.MinTypedLength} characters."),
                DumpPasswordSetOutcome.NotPrintableAscii => DumpPasswordPageResult<int>.Fail(DumpPasswordPageOutcome.Invalid,
                    "The password may contain only printable ASCII characters: Latin letters, digits, punctuation and spaces."),
                DumpPasswordSetOutcome.TooLong => DumpPasswordPageResult<int>.Fail(DumpPasswordPageOutcome.Invalid, TooLongMessage),
                DumpPasswordSetOutcome.StoredRowUnreadable => DumpPasswordPageResult<int>.Fail(DumpPasswordPageOutcome.Invalid, UnreadableMessage),
                // ProductUnknown (removed meanwhile) and NotRootProduct (cannot happen: the root id is passed) answer 404.
                _ => DumpPasswordPageResult<int>.Fail(DumpPasswordPageOutcome.NotFound),
            };
        }


        internal static bool LooksGenerated(string password) =>
            password.Length == DumpPasswordRules.GeneratedLength && password.All(symbol => DumpPasswordRules.GeneratedAlphabet.Contains(symbol));


        private bool TryAuthorize(User user, Guid productId, out Guid rootId, out DumpPasswordPageOutcome failure)
        {
            failure = DumpPasswordPageOutcome.NotFound;

            // A product the user cannot see answers as an unknown one: its existence is not disclosed.
            if (!TryGetRoot(productId, out rootId) || !UserTreeAccess.IsRootProductVisibleTo(user, rootId))
                return false;

            failure = DumpPasswordPageOutcome.Forbidden;

            return UserTreeAccess.CanManageRootProduct(user, rootId);
        }

        private bool TryGetRoot(Guid productId, out Guid rootId)
        {
            rootId = Guid.Empty;

            if (!_cache.TryGetProduct(productId, out var product) || product is null)
                return false;

            rootId = product.Root.Id;
            return true;
        }

        private List<T> ListVersions<T>(Guid rootId, Func<DumpPasswordVersion, T> map) =>
            (_passwords.GetVersions(rootId) ?? []).OrderByDescending(version => version.Version).Select(map).ToList();

        private DateTime? KeptUntil(DumpPasswordVersion version) => version.RetiredUtc is { } retired ? retired + _retention : null;
    }
}
