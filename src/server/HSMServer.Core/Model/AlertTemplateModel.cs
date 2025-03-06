using System;
using System.Collections.Generic;
using System.Linq;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model.Policies;
using HSMServer.PathTemplates;


namespace HSMServer.Core.Model
{
    public sealed class AlertTemplateModel
    {
        public const byte AnyType = 100;

        private PathTemplateConverter _pathTemplateConverter = new PathTemplateConverter();

        public TimeIntervalModel TTL { get; set; }

        public TTLPolicy TTLPolicy { get; set; }

        public List<Policy> Policies { get; set; } = [];

        public Guid Id { get; set; }

        public string Name { get; set; }

        public string Path { get; set; }

        public byte SensorType { get; set; }

        public bool IsMatch(string path) => _pathTemplateConverter.IsMatch(path);

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

            if (entity.TTLPolicy != null)
            {
                TTLPolicy = new TTLPolicy();
                TTLPolicy.Apply(entity.TTLPolicy);
            }

            if (entity.Policies != null)
            {
                Policies = new List<Policy>();
                foreach (PolicyEntity item in entity.Policies)
                {
                    var pol = Policy.BuildPolicy(SensorType);
                    pol.Apply(item);
                    Policies.Add(pol);
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
                Policies = Policies?.Select(x => x.ToEntity()).ToList() ?? [],
                SensorType = SensorType,
            };
        }

    }
}