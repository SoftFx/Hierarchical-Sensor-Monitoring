﻿using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.DataLayer;

namespace HSMServer.Core.Model
{
    public sealed class IntegerSensorModel : BaseSensorModel<IntegerValue>
    {
        public override SensorType Type { get; } = SensorType.Integer;

        public override IntegerValuesStorage Storage { get; }


        internal IntegerSensorModel(string productId, string sensorName) : base(productId, sensorName) { }

        internal IntegerSensorModel(SensorEntity entity, IDatabaseCore db)
            : base(entity)
        {
            Storage = new IntegerValuesStorage() { Database = db };
        }
    }
}
