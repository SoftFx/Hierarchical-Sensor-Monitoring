using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Journal;
using HSMServer.Core.Model;
using HSMServer.Core.TableOfChanges;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;

namespace HSMServer.Core.LargeFiles.DumpPasswords
{
    /// <summary>
    /// The dump passwords of products (ADR-0022 point 13), kept in memory and stored in the configuration database as a row
    /// of their own per product. Every change runs under one lock, writes the database first and changes memory only after
    /// the write landed, so two changes at once get consecutive versions that are both durable, and a failed write changes
    /// nothing. No log line, journal record or exception text of this class carries a password: they name the product and
    /// the version only.
    /// </summary>
    public sealed class DumpPasswordService : IDumpPasswordService, IDisposable
    {
        public static readonly TimeSpan DefaultRetention = TimeSpan.FromDays(30);

        internal const string JournalEnviroment = "Dump password update";
        internal const string JournalPropertyName = "Dump password";

        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        private readonly object _lock = new();

        // The kept versions of each product, newest first; an array is never changed once it is published here.
        private readonly Dictionary<Guid, DumpPasswordVersion[]> _versions = [];

        private readonly IDatabaseCore _database;
        private readonly IJournalService _journal;
        private readonly ITreeValuesCache _cache;
        private readonly TimeSpan _retention;
        private readonly Func<DateTime> _clock;


        public event Action<Guid, int> PasswordChanged;


        public DumpPasswordService(IDatabaseCore database, IJournalService journal, ITreeValuesCache cache, TimeSpan? retention = null)
            : this(database, journal, cache, retention, null) { }

        // clock: the time a version is set at (test seam); null: the system clock.
        internal DumpPasswordService(IDatabaseCore database, IJournalService journal, ITreeValuesCache cache, TimeSpan? retention, Func<DateTime> clock)
        {
            _database = database ?? throw new ArgumentNullException(nameof(database));
            _journal = journal ?? throw new ArgumentNullException(nameof(journal));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _retention = retention ?? DefaultRetention;
            _clock = clock ?? (() => DateTime.UtcNow);

            if (_retention < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(retention), "The retention of replaced dump passwords cannot be negative.");

            foreach (var entity in _database.GetAllDumpPasswords())
            {
                var versions = FromEntity(entity);

                if (versions.Length > 0)
                    _versions[entity.ProductId] = versions;
            }

            _cache.ChangeProductEvent += ChangeProductHandler;

            _logger.Info($"Dump passwords loaded for {_versions.Count} products");
        }


        public void Dispose() => _cache.ChangeProductEvent -= ChangeProductHandler;


        public DumpPasswordVersion GetCurrent(Guid productId)
        {
            lock (_lock)
                return _versions.TryGetValue(productId, out var versions) ? versions[0] : null;
        }

        public DumpPasswordVersion GetVersion(Guid productId, int version)
        {
            lock (_lock)
                return _versions.TryGetValue(productId, out var versions) ? Array.Find(versions, v => v.Version == version) : null;
        }

        public IReadOnlyList<DumpPasswordVersion> GetVersions(Guid productId)
        {
            lock (_lock)
                return _versions.TryGetValue(productId, out var versions) ? Array.AsReadOnly(versions) : [];
        }

        public bool HasPassword(Guid productId)
        {
            lock (_lock)
                return _versions.ContainsKey(productId);
        }

        public IReadOnlyCollection<Guid> GetProductIds()
        {
            lock (_lock)
                return [.. _versions.Keys];
        }

        public DumpPasswordSetResult SetPassword(Guid productId, string password, bool generated, InitiatorInfo initiator)
        {
            var product = _cache.GetProduct(productId);

            if (product is null)
                return new DumpPasswordSetResult(DumpPasswordSetOutcome.ProductUnknown, 0);

            if (!product.IsRoot)
                return new DumpPasswordSetResult(DumpPasswordSetOutcome.NotRootProduct, 0);

            var outcome = Validate(password, generated);

            if (outcome != DumpPasswordSetOutcome.Set)
                return new DumpPasswordSetResult(outcome, 0);

            initiator ??= InitiatorInfo.System;

            int version;

            lock (_lock)
            {
                var kept = _versions.GetValueOrDefault(productId) ?? [];

                version = (kept.Length > 0 ? kept[0].Version : 0) + 1;

                DumpPasswordVersion[] next = [new DumpPasswordVersion(version, password, _clock(), initiator.ToString(), null), .. kept];

                Store(productId, next, version);

                _versions[productId] = next;
            }

            _logger.Info($"The dump password of product {productId} is changed: version {version}");

            WriteJournal(productId, product, version, initiator);
            RaisePasswordChanged(productId, version);

            return new DumpPasswordSetResult(DumpPasswordSetOutcome.Set, version);
        }

        public string Generate() => RandomNumberGenerator.GetString(DumpPasswordRules.GeneratedAlphabet, DumpPasswordRules.GeneratedLength);

        /// <summary>
        /// Sets the retirement time once. The current version, an unknown version and a version retired already are left as
        /// they are: only a replaced version stops being carried by archives. Throws when the database cannot be written.
        /// </summary>
        public void MarkRetired(Guid productId, int version, DateTime utc)
        {
            lock (_lock)
            {
                if (!_versions.TryGetValue(productId, out var versions))
                    return;

                var index = Array.FindIndex(versions, v => v.Version == version);

                if (index <= 0 || versions[index].IsRetired)
                    return;

                var next = (DumpPasswordVersion[])versions.Clone();
                next[index] = versions[index] with { RetiredUtc = utc };

                Store(productId, next, version);

                _versions[productId] = next;
            }

            _logger.Info($"Dump password version {version} of product {productId} is retired");
        }

        /// <summary>
        /// Removes the replaced versions retired longer than the retention ago; the current version stays. A product whose row
        /// cannot be written keeps its versions in memory and on disk (the next run tries again); the other products go on,
        /// and the failures are thrown together at the end.
        /// </summary>
        public int RemoveExpired(DateTime nowUtc)
        {
            var removed = 0;
            List<Exception> failures = null;

            lock (_lock)
            {
                foreach (var productId in _versions.Keys.ToArray())
                {
                    var versions = _versions[productId];
                    var next = versions.Where((v, i) => i == 0 || !IsExpired(v, nowUtc)).ToArray();

                    if (next.Length == versions.Length)
                        continue;

                    try
                    {
                        Store(productId, next, next[0].Version);
                    }
                    catch (Exception ex)
                    {
                        (failures ??= []).Add(ex);
                        continue;
                    }

                    _versions[productId] = next;
                    removed += versions.Length - next.Length;

                    _logger.Info($"Dump passwords of product {productId}: {versions.Length - next.Length} expired versions removed");
                }
            }

            if (failures is not null)
                throw new AggregateException($"The expired dump passwords of {failures.Count} products could not be removed", failures);

            return removed;
        }

        /// <summary>Removes the row of the product, then the versions in memory. Throws when the database cannot be written.</summary>
        public void RemoveProduct(Guid productId)
        {
            lock (_lock)
            {
                try
                {
                    // Also when nothing is in memory: a row the load skipped is removed with its product as well.
                    _database.RemoveDumpPasswords(productId);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"The dump passwords of product {productId} could not be removed");
                    throw;
                }

                if (!_versions.Remove(productId))
                    return;
            }

            _logger.Info($"The dump passwords of product {productId} are removed");
        }


        internal static DumpPasswordSetOutcome Validate(string password, bool generated)
        {
            // A password can be replaced, never cleared: nothing is not a password, even a generated one.
            if (string.IsNullOrEmpty(password))
                return DumpPasswordSetOutcome.TooShort;

            if (generated)
                return DumpPasswordSetOutcome.Set;

            if (password.Length < DumpPasswordRules.MinTypedLength)
                return DumpPasswordSetOutcome.TooShort;

            foreach (var c in password)
                if (c < '\x20' || c > '\x7E')
                    return DumpPasswordSetOutcome.NotPrintableAscii;

            if (password.Length > DumpPasswordRules.MaxLength)
                return DumpPasswordSetOutcome.TooLong;

            return DumpPasswordSetOutcome.Set;
        }

        private bool IsExpired(DumpPasswordVersion version, DateTime nowUtc) =>
            version.RetiredUtc is { } retired && retired + _retention < nowUtc;

        // Under _lock. Throws when the row cannot be written; logs the product and the version only.
        private void Store(Guid productId, DumpPasswordVersion[] versions, int version)
        {
            try
            {
                _database.PutDumpPasswords(ToEntity(productId, versions));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"The dump passwords of product {productId} could not be stored (version {version})");
                throw;
            }
        }

        private void WriteJournal(Guid productId, ProductModel product, int version, InitiatorInfo initiator)
        {
            // Journal appends run after the primary write and never fail it (the journal feature's durability rule).
            try
            {
                _journal.AddRecord(new JournalRecordModel(productId, initiator)
                {
                    Enviroment = JournalEnviroment,
                    PropertyName = JournalPropertyName,
                    OldValue = null,
                    NewValue = $"changed (version {version})",
                    Path = product.FullPath,
                    Kind = JournalKinds.NodeSettings,
                    ProductId = productId.ToString(),
                });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"The journal record of dump password version {version} of product {productId} could not be written");
            }
        }

        private void RaisePasswordChanged(Guid productId, int version)
        {
            var handlers = PasswordChanged;

            if (handlers is null)
                return;

            // One subscriber that throws must neither fail a change that is durable already nor keep the others from hearing it.
            foreach (Action<Guid, int> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(productId, version);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"A handler of the change of the dump password of product {productId} (version {version}) failed");
                }
            }
        }

        private void ChangeProductHandler(ProductModel product, ActionType action)
        {
            if (action != ActionType.Delete || product is null)
                return;

            // Called by the cache in the middle of its removal: a failure is logged here, never thrown into the cache.
            try
            {
                RemoveProduct(product.Id);
            }
            catch (Exception)
            {
                // RemoveProduct logged it.
            }
        }

        private static ProductDumpPasswordsEntity ToEntity(Guid productId, DumpPasswordVersion[] versions) => new()
        {
            ProductId = productId,
            Versions = versions.Select(v => new DumpPasswordVersionEntity
            {
                Version = v.Version,
                Password = v.Password,
                SetTicks = v.SetUtc.Ticks,
                SetBy = v.SetBy,
                RetiredTicks = v.RetiredUtc?.Ticks,
            }).ToList(),
        };

        private static DumpPasswordVersion[] FromEntity(ProductDumpPasswordsEntity entity) =>
            (entity.Versions ?? [])
                .Where(v => v is not null)
                .Select(v => new DumpPasswordVersion(v.Version, v.Password, new DateTime(v.SetTicks, DateTimeKind.Utc), v.SetBy,
                    v.RetiredTicks is { } retired ? new DateTime(retired, DateTimeKind.Utc) : null))
                .OrderByDescending(v => v.Version)
                .ToArray();
    }
}
