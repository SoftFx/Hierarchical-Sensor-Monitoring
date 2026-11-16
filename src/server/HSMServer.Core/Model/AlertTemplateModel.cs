using System;
using System.Collections.Generic;
using System.Linq;
using HSMCommon.Model;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model.NodeSettings;
using HSMServer.Core.Model.Policies;
using HSMServer.PathTemplates;
using NLog;


namespace HSMServer.Core.Model
{
    public sealed class AlertTemplateModel
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        public const byte AnyType = 100;

        private readonly bool _logSkippedPathTemplates;

        private volatile List<PathTemplateConverter> _pathConverters = [];

        public List<TtlEntry> TtlEntries { get; set; } = [];

        public List<Policy> Policies { get; set; } = [];

        public Guid Id { get; set; }

        public string Name { get; set; }

        public List<string> Paths { get; set; } = [];

        // Backward-compatible access to the first path.
        // Setter replaces all paths with a single entry; null is ignored to
        // avoid wiping a multi-path list when the old Path property is read-then-written.
        public string Path
        {
            get => Paths.FirstOrDefault() ?? string.Empty;
            set { if (value != null) Paths = [value]; }
        }

        public byte SensorType { get; set; }

        public Guid FolderId { get; set; }


        public AlertTemplateModel()
        {
            Id = Guid.NewGuid();
        }

        public AlertTemplateModel(AlertTemplateEntity entity, bool logSkippedPathTemplates = true)
        {
            _logSkippedPathTemplates = logSkippedPathTemplates;

            Id = new Guid(entity.Id);
            Name = entity.Name;
            SensorType = entity.SensorType;
            FolderId = entity.FolderId;

            // Migration: handle both new Paths list and legacy single Path
            Paths = entity.Paths?.Count > 0
                ? entity.Paths
                : entity.Path != null ? [entity.Path] : [];

            TtlEntries = [];

            // Migration: handle both old single TTLPolicy and new TTLPolicies list
            var ttlEntities = entity.TTLPolicies?.Count > 0
                ? entity.TTLPolicies
                : entity.TTLPolicy != null ? [entity.TTLPolicy] : null;

            if (ttlEntities != null)
            {
                var ttlIntervals = entity.TTLs?.Count > 0
                    ? entity.TTLs
                    : entity.TTL != null ? [entity.TTL] : null;

                for (int i = 0; i < ttlEntities.Count; i++)
                {
                    var ttlEntity = ttlEntities[i];
                    var interval = ttlIntervals != null && i < ttlIntervals.Count
                        ? ttlIntervals[i]
                        : null;

                    TimeIntervalSettingProperty ttl = new TimeIntervalSettingProperty();
                    if (interval != null)
                        ttl.TrySetValue(new TimeIntervalModel(interval));

                    TtlEntries.Add(new TtlEntry(new TTLPolicy(ttl, ttlEntity), ttl.Value ?? TimeIntervalModel.None));
                }
            }

            if (entity.Policies != null)
            {
                Policies = new List<Policy>();
                foreach (PolicyEntity item in entity.Policies)
                {
                    var policy = Policy.BuildPolicy(SensorType);
                    policy.Apply(item);
                    Policies.Add(policy);
                }
            }

            TryApplyPathTemplates(out _);
        }

        public bool TryApplyPathTemplates(out string error)
        {
            error = null;
            var converters = new List<PathTemplateConverter>();

            foreach (var path in Paths)
            {
                if (string.IsNullOrEmpty(path))
                    continue;

                var converter = new PathTemplateConverter();
                if (!converter.ApplyNewTemplate(path, out var pathError))
                {
                    // Warn is the trace for corrupt PERSISTED templates found at
                    // load/restore time. A model built to VALIDATE client input
                    // (API/MCP write, web-UI save) answers the same reason in its
                    // 400/422 — logging it at Warn on every attempt would be
                    // operator-log noise for an ordinary client error (#1607
                    // review), so those builds log at Debug instead.
                    if (_logSkippedPathTemplates)
                        _logger.Warn($"Alert template '{Name}': skipping invalid path template '{path}': {pathError}");
                    else
                        _logger.Debug($"Alert template '{Name}': skipping invalid path template '{path}': {pathError}");

                    error = pathError;
                    continue;
                }
                converters.Add(converter);
            }

            _pathConverters = converters;
            return error == null;
        }

        public AlertTemplateEntity ToEntity()
        {
            return new AlertTemplateEntity()
            {
                Id = Id.ToByteArray(),
                Name = Name,
                Paths = Paths,
                TTLPolicies = TtlEntries?.Select(e => e.Policy.ToEntity()).ToList() ?? [],
                TTLs = TtlEntries?.Select(e => e.Interval?.ToEntity()).ToList() ?? [],
                Policies = Policies?.Select(x => x.ToEntity()).ToList() ?? [],
                SensorType = SensorType,
                FolderId = FolderId,
            };
        }

        public SensorType? GetSensorType() => SensorType == AnyType ? null : (SensorType)SensorType;

        public bool IsMatch(BaseSensorModel sensor) => IsMatch(sensor.FullPath, sensor.Type, sensor.Root.FolderId);

        // The same decision for a sensor that is not (yet) at that path: the
        // sensor-move dry run asks what would match at the destination (#1721).
        public bool IsMatch(string fullPath, SensorType type, Guid? rootFolderId)
        {
            if (_pathConverters.Count == 0)
                return false;

            if (!_pathConverters.Any(c => c.IsMatch(fullPath)))
                return false;

            if (GetSensorType().HasValue && GetSensorType() != type)
                return false;

            if (rootFolderId != FolderId)
                return false;

            return true;
        }
    }
}
