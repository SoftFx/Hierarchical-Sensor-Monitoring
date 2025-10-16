using System.Collections.Generic;
using HSMDatabase.AccessManager.DatabaseEntities.VisualEntity;


namespace HSMDatabase.AccessManager.DatabaseEntities
{
    public sealed record SensorEntity : BaseNodeEntity
    {
        public string ProductId { get; init; }


        public byte Type { get; init; }

        public byte State { get; init; }


        public bool AggregateValues { get; init; }

        public int? OriginalUnit { get; init; }

        public int? DisplayUnit { get; init; }

        public bool IsSingleton { get; init; }

        public long EndOfMuting { get; init; }

        public int Integration { get; init; }

        public int Statistics { get; init; }

        public Dictionary<int, EnumOptionEntity> EnumOptions { get; init; }

        public TableSettingEntity TableSettings { get; init; }
    }
}