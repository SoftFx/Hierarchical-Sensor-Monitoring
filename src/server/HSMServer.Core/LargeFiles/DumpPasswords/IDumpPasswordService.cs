using HSMServer.Core.TableOfChanges;
using System;
using System.Collections.Generic;

namespace HSMServer.Core.LargeFiles.DumpPasswords
{
    /// <summary>
    /// One version of a product's dump password (ADR-0022 point 13). <see cref="ToString"/> never prints the value, so the
    /// record is safe in a log line or an exception text.
    /// </summary>
    public sealed record DumpPasswordVersion(int Version, string Password, DateTime SetUtc, string SetBy, DateTime? RetiredUtc)
    {
        /// <summary>True once no archive carries this replaced version; it stays readable until the retention ends.</summary>
        public bool IsRetired => RetiredUtc is not null;

        public override string ToString() => $"dump password version {Version} (set {SetUtc:u} by {SetBy}{(RetiredUtc is null ? "" : $", retired {RetiredUtc:u}")})";
    }


    public enum DumpPasswordSetOutcome : byte
    {
        Set,

        /// <summary>A typed password shorter than <see cref="DumpPasswordRules.MinTypedLength"/>.</summary>
        TooShort,

        /// <summary>A typed password with a character outside printable ASCII.</summary>
        NotPrintableAscii,

        /// <summary>A password longer than <see cref="DumpPasswordRules.MaxLength"/>.</summary>
        TooLong,

        ProductUnknown,

        /// <summary>The product is not a root product: the password belongs to the root, whose archives it protects.</summary>
        NotRootProduct,
    }


    public sealed record DumpPasswordSetResult(DumpPasswordSetOutcome Outcome, int Version)
    {
        public bool IsOk => Outcome == DumpPasswordSetOutcome.Set;
    }


    /// <summary>The rules of a dump password: what is generated and what a person may type.</summary>
    public static class DumpPasswordRules
    {
        public const int GeneratedLength = 24;

        public const int MinTypedLength = 16;

        public const int MaxLength = 256;

        /// <summary>Letters and digits only: nothing an archiver encodes differently, nothing hard to type.</summary>
        public const string GeneratedAlphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
    }


    /// <summary>
    /// The dump passwords of products, stored as they are in the configuration database apart from the product entity, so
    /// the value never reaches a product model, a view model, a change set or an API answer. Read only by the archive
    /// builder and by the signed-in settings page of a Manager or an admin.
    /// </summary>
    public interface IDumpPasswordService
    {
        /// <summary>Raised after a new version is stored durably: (product, version).</summary>
        event Action<Guid, int> PasswordChanged;

        /// <summary>The version new archives of the product are built under; null when the product has none.</summary>
        DumpPasswordVersion GetCurrent(Guid productId);

        /// <summary>A kept version (current or replaced and not yet removed); null when it is not kept.</summary>
        DumpPasswordVersion GetVersion(Guid productId, int version);

        /// <summary>Every kept version, newest first: what the settings page shows to a Manager or an admin.</summary>
        IReadOnlyList<DumpPasswordVersion> GetVersions(Guid productId);

        bool HasPassword(Guid productId);

        /// <summary>The products that have a dump password: the builder retires their replaced versions after a restart.</summary>
        IReadOnlyCollection<Guid> GetProductIds();

        /// <summary>
        /// Stores a new version (past both the highest kept version and the highest version an archive of the product carries)
        /// durably, then writes "dump password changed" to the journal without any value. A password can be replaced, never
        /// cleared. Throws when the configuration database cannot be written or the archives cannot be listed.
        /// </summary>
        DumpPasswordSetResult SetPassword(Guid productId, string password, bool generated, InitiatorInfo initiator);

        /// <summary>A new password of <see cref="DumpPasswordRules.GeneratedLength"/> characters from a cryptographic generator.</summary>
        string Generate();

        /// <summary>Marks a replaced version as no longer carried by any archive; kept readable until the retention ends.</summary>
        void MarkRetired(Guid productId, int version, DateTime utc);

        /// <summary>
        /// Removes replaced versions retired for longer than the retention; returns how many were removed. A version in
        /// <paramref name="carried"/> (the (product, version) pairs the packed archives carry, from one listing) is kept.
        /// </summary>
        int RemoveExpired(DateTime nowUtc, IReadOnlySet<(Guid ProductId, int Version)> carried);

        /// <summary>Removes every version of a product (with the product).</summary>
        void RemoveProduct(Guid productId);
    }
}
