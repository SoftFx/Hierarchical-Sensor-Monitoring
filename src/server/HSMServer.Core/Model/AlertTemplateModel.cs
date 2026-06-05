using System;
using System.Collections.Generic;
using System.Linq;
using HSMCommon.Model;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model.NodeSettings;
using HSMServer.Core.Model.Policies;
using HSMServer.PathTemplates;


namespace HSMServer.Core.Model
{
    public sealed class AlertTemplateModel
    {
        public const byte AnyType = 100;

        private List<PathTemplateConverter> _pathConverters = [];

        public List<TtlEntry> TtlEntries { get; set; } = [];

        public List<Policy> Policies { get; set; } = [];

        public Guid Id { get; set; }

        public string Name { get; set; }

        public List<string> Paths { get; set; } = [];

        // Backward-compatible access to the first path
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

        public AlertTemplateModel(AlertTemplateEntity entity)
        {
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
            _pathConverters = [];

            foreach (var path in Paths)
            {
                if (string.IsNullOrEmpty(path))
                    continue;

                var converter = new PathTemplateConverter();
                if (!converter.ApplyNewTemplate(path, out var pathError))
                {
                    error = pathError;
                    return false;
                }
                _pathConverters.Add(converter);
            }

            return true;
        }

        public bool TryApplyPathTemplate(string path, out string error)
        {
            Paths = string.IsNullOrEmpty(path) ? [] : [path];
            return TryApplyPathTemplates(out error);
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

        public bool IsMatch(BaseSensorModel sensor)
        {
            if (_pathConverters.Count == 0)
                return false;

            if (!_pathConverters.Any(c => c.IsMatch(sensor.FullPath)))
                return false;

            if (GetSensorType().HasValue && GetSensorType() != sensor.Type)
                return false;

            if (sensor.Root.FolderId != FolderId)
                return false;

            return true;
        }
    }
}
