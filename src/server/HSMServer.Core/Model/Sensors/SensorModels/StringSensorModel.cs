﻿using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.DataLayer;

namespace HSMServer.Core.Model
{
    public sealed class StringSensorModel : BaseSensorModel<StringValue>
    {
        public override SensorType Type { get; } = SensorType.String;

        public override StringValuesStorage Storage { get; }


        internal StringSensorModel(SensorEntity entity, IDatabaseCore db)
            : base(entity)
        {
            Storage = new StringValuesStorage(db);
        }
    }
}
