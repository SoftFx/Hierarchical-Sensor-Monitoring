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

        public TimeIntervalModel TTL { get; set; } = TimeIntervalModel.None;

        public TTLPolicy TTLPolicy { get; set; }

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

            if (entity.TTLPolicy != null)
            {
                TimeIntervalSettingProperty ttl = new TimeIntervalSettingProperty();
                ttl.TrySetValue(new TimeIntervalModel(entity.TTL));
                TTLPolicy = new TTLPolicy(ttl, entity.TTLPolicy);
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
                TTLPolicy = TTLPolicy?.ToEntity(),
                TTL = TTL?.ToEntity(),
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