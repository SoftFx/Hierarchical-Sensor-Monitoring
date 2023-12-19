using HSMCommon.Extensions;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMSensorDataObjects.SensorRequests;
using HSMServer.Core.Cache.UpdateEntities;
using System;

namespace HSMServer.Core.Model.Policies
{
    public sealed class PolicySchedule
    {
        public DateTime Time { get; private set; }

        public AlertRepeatMode RepeatMode { get; private set; }


        internal PolicySchedule() { }

        internal PolicySchedule(PolicyScheduleEntity entity)
        {
            if (entity is null)
                return;

            Time = new DateTime(entity.TimeTicks);
            RepeatMode = (AlertRepeatMode)entity.RepeateMode;
        }


        internal void Update(PolicyScheduleUpdate update)
        {
            if (update is null)
                return;

            Time = update.Time ?? Time;
            RepeatMode = update.RepeatMode ?? RepeatMode;
        }


        internal DateTime GetSendTime()
        {
            if (RepeatMode == AlertRepeatMode.None)
                return Time;

            var shiftTime = RepeatMode switch
            {
                AlertRepeatMode.Hourly => TimeSpan.FromHours(1),
                AlertRepeatMode.Dayly => TimeSpan.FromDays(1),
                AlertRepeatMode.Weekly => TimeSpan.FromDays(7),
            };

            return Time.Ceil(shiftTime);
        }

        internal PolicyScheduleEntity ToEntity() =>
            new()
            {
                TimeTicks = Time.Ticks,
                RepeateMode = (byte)RepeatMode,
            };
    }
}