using System;
using System.Collections.Generic;
using System.Linq;
using HSMServer.Core.LargeFiles.DumpPasswords;
using HSMServer.Core.TableOfChanges;

namespace HSMServer.Core.Tests.LargeFiles.Archives
{
    /// <summary>The dump passwords in memory, with a 30-day retention of retired versions.</summary>
    internal sealed class FakeDumpPasswordService : IDumpPasswordService
    {
        public static readonly TimeSpan Retention = TimeSpan.FromDays(30);

        private readonly object _lock = new();
        private readonly Dictionary<Guid, List<DumpPasswordVersion>> _versions = new();


        public Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;

        public List<(Guid ProductId, int Version, DateTime Utc)> Retired { get; } = new();

        public int RemoveExpiredCalls { get; private set; }


        public event Action<Guid, int> PasswordChanged;


        public int Set(Guid productId, string password) => SetPassword(productId, password, false, InitiatorInfo.System).Version;

        /// <summary>Drops a kept version without retention, as if it were lost.</summary>
        public void Lose(Guid productId, int version)
        {
            lock (_lock)
                _versions[productId].RemoveAll(v => v.Version == version);
        }


        public DumpPasswordVersion GetCurrent(Guid productId)
        {
            lock (_lock)
                return _versions.TryGetValue(productId, out var list) ? list.OrderByDescending(v => v.Version).FirstOrDefault() : null;
        }

        public DumpPasswordVersion GetVersion(Guid productId, int version)
        {
            lock (_lock)
                return _versions.TryGetValue(productId, out var list) ? list.FirstOrDefault(v => v.Version == version) : null;
        }

        public IReadOnlyList<DumpPasswordVersion> GetVersions(Guid productId)
        {
            lock (_lock)
                return _versions.TryGetValue(productId, out var list) ? list.OrderByDescending(v => v.Version).ToList() : [];
        }

        public bool HasPassword(Guid productId) => GetCurrent(productId) is not null;

        public DumpPasswordSetResult SetPassword(Guid productId, string password, bool generated, InitiatorInfo initiator)
        {
            int version;
            lock (_lock)
            {
                if (!_versions.TryGetValue(productId, out var list))
                    _versions[productId] = list = new List<DumpPasswordVersion>();

                version = list.Count == 0 ? 1 : list.Max(v => v.Version) + 1;
                list.Add(new DumpPasswordVersion(version, password, UtcNow(), "test", null));
            }

            PasswordChanged?.Invoke(productId, version);

            return new DumpPasswordSetResult(DumpPasswordSetOutcome.Set, version);
        }

        public string Generate() => Guid.NewGuid().ToString("N")[..DumpPasswordRules.GeneratedLength];

        public void MarkRetired(Guid productId, int version, DateTime utc)
        {
            lock (_lock)
            {
                var list = _versions[productId];
                var index = list.FindIndex(v => v.Version == version);
                list[index] = list[index] with { RetiredUtc = utc };
                Retired.Add((productId, version, utc));
            }
        }

        public int RemoveExpired(DateTime nowUtc)
        {
            lock (_lock)
            {
                RemoveExpiredCalls++;

                return _versions.Values.Sum(list => list.RemoveAll(v => v.RetiredUtc is { } retired && nowUtc - retired > Retention));
            }
        }

        public void RemoveProduct(Guid productId)
        {
            lock (_lock)
                _versions.Remove(productId);
        }
    }
}
