﻿using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.DataLayer;

namespace HSMServer.Core.Model
{
    public sealed class BooleanSensorModel : BaseSensorModel<BooleanValue>
    {
        public override SensorType Type { get; } = SensorType.Boolean;

        public override BooleanValuesStorage Storage { get; }


        internal BooleanSensorModel(SensorEntity entity, IDatabaseCore db)
            : base(entity)
        {
            Storage = new BooleanValuesStorage(db);
        }
    }
}
