using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.SensorsDataValidation;
using System;

namespace HSMServer.Core.Model
{
    public abstract class Policy
    {
        public Guid Id { get; init; }

        public string Type { get; init; }


        protected Policy()
        {
            Id = Guid.NewGuid();
            Type = GetType().Name;
        }


        internal PolicyEntity ToEntity() =>
            new()
            {
                Id = Id.ToString(),
                Policy = this,
            };

        internal abstract ValidationResult Validate<T>(T value) where T : BaseValue;
    }
}
