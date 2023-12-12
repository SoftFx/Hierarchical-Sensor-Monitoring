using HSMDatabase.AccessManager.DatabaseEntities;
using System;

namespace HSMServer.Core.Model.Policies
{
    public enum AlertRepeateMode : byte
    {
        None = 0,

        Hourly = 20,
        Dayly = 50,
        Weekly = 100,
        Monthly = 150,
    }


    public sealed class PolicySchedule
    {
        public DateTime Time { get; private set; }

        public AlertRepeateMode RepeateMode { get; private set; }


        internal PolicySchedule() { }

        internal PolicySchedule(PolicyScheduleEntity entity)
        {
            if (entity is null)
                return;

            Time = new DateTime(entity.TimeTicks);
            RepeateMode = (AlertRepeateMode)entity.RepeateMode;
        }


        internal PolicyScheduleEntity ToEntity() =>
            new()
            {
                TimeTicks = Time.Ticks,
                RepeateMode = (byte)RepeateMode,
            };
    }
}