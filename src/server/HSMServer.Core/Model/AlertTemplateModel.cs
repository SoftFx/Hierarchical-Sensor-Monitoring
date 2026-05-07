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

        private PathTemplateConverter _pathTemplateConverter = new PathTemplateConverter();

        public List<TimeIntervalModel> TTLs { get; set; } = [];

        public List<TTLPolicy> TTLPolicies { get; set; } = [];

        public List<Policy> Policies { get; set; } = [];

        public Guid Id { get; set; }

        public string Name { get; set; }

        public string Path { get; set; }

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
            Path = entity.Path;
            SensorType = entity.SensorType;
            FolderId = entity.FolderId;

            TTLPolicies = [];
            TTLs = [];

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

                    TTLPolicies.Add(new TTLPolicy(ttl, ttlEntity));
                    TTLs.Add(ttl.Value ?? TimeIntervalModel.None);
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

            TryApplyPathTemplate(Path, out _);
        }

        public bool TryApplyPathTemplate(string path, out string error)
        {
            return _pathTemplateConverter.ApplyNewTemplate(path, out error);
        }

        public AlertTemplateEntity ToEntity()
        {
            return new AlertTemplateEntity()
            {
                Id = Id.ToByteArray(),
                Name = Name,
                Path = Path,
                TTLPolicies = TTLPolicies?.Select(x => x.ToEntity()).ToList() ?? [],
                TTLs = TTLs?.Select(x => x?.ToEntity()).ToList() ?? [],
                Policies = Policies?.Select(x => x.ToEntity()).ToList() ?? [],
                SensorType = SensorType,
                FolderId = FolderId,
            };
        }

        public SensorType? GetSensorType() => SensorType == AnyType ? null : (SensorType)SensorType;

        public bool IsMatch(BaseSensorModel sensor)
        {
            if (!_pathTemplateConverter.IsMatch(sensor.FullPath))
                return false;

            if (GetSensorType().HasValue && GetSensorType() != sensor.Type)
                return false;

            if (sensor.Root.FolderId != FolderId)
                return false;

            return true;
        }
    }
}
