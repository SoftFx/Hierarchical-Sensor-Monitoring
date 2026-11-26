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
        private readonly Dictionary<Guid, int> _pinnedCurrent = new();


        public Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;

        public List<(Guid ProductId, int Version, DateTime Utc)> Retired { get; } = new();

        public int RemoveExpiredCalls { get; private set; }

        /// <summary>The (product, version) pairs the packed archives carried, as the last RemoveExpired was given them.</summary>
        public IReadOnlySet<(Guid ProductId, int Version)> LastCarried { get; private set; }


        public event Action<Guid, int> PasswordChanged;


        public int Set(Guid productId, string password) => SetPassword(productId, password, false, InitiatorInfo.System).Version;

        /// <summary>Drops a kept version without retention, as if it were lost.</summary>
        public void Lose(Guid productId, int version)
        {
            lock (_lock)
                _versions[productId].RemoveAll(v => v.Version == version);
        }


        /// <summary>Makes a kept version current although a higher one is kept: a state the real service never reaches.</summary>
        public void PinCurrent(Guid productId, int version)
        {
            lock (_lock)
                _pinnedCurrent[productId] = version;
        }


        public DumpPasswordVersion GetCurrent(Guid productId)
        {
            lock (_lock)
            {
                if (!_versions.TryGetValue(productId, out var list))
                    return null;

                return _pinnedCurrent.TryGetValue(productId, out var pinned)
                    ? list.First(v => v.Version == pinned)
                    : list.OrderByDescending(v => v.Version).FirstOrDefault();
            }
        }

        public DumpPasswordVersion GetVersion(Guid productId, int version)
        {
            lock (_lock)
                return _versions.TryGetValue(productId, out var list) ? list.FirstOrDefault(v => v.Version == version) : null;
        }

        public IReadOnlyCollection<Guid> GetProductIds()
        {
            lock (_lock)
                return [.. _versions.Keys];
        }

        public IReadOnlyList<DumpPasswordVersion> GetVersions(Guid productId)
        {
            lock (_lock)
                return _versions.TryGetValue(productId, out var list) ? list.OrderByDescending(v => v.Version).ToList() : [];
        }

        public bool HasPassword(Guid productId) => GetCurrent(productId) is not null;

        public bool IsUnreadable(Guid productId) => false;

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

        public int RemoveExpired(DateTime nowUtc, IReadOnlySet<(Guid ProductId, int Version)> carried)
        {
            lock (_lock)
            {
                RemoveExpiredCalls++;
                LastCarried = carried;

                return _versions.Sum(pair => pair.Value.RemoveAll(v =>
                    v.RetiredUtc is { } retired && nowUtc - retired > Retention && !carried.Contains((pair.Key, v.Version))));
            }
        }

        public void RemoveProduct(Guid productId)
        {
            lock (_lock)
                _versions.Remove(productId);
        }
    }
}
