using HSMCommon.Extensions;
using HSMDatabase.AccessManager.DatabaseEntities;
using System;
using HSMServer.Core.Cache.UpdateEntities;

namespace HSMServer.Core.Model.Policies
{
    public enum AlertRepeateMode : byte
    {
        None = 0,

        Hourly = 20,
        Dayly = 50,
        Weekly = 100,
    }


    public sealed class PolicySchedule
    {
        public DateTime Time { get; private set; }

        public AlertRepeateMode RepeatMode { get; private set; }


        internal PolicySchedule() { }

        internal PolicySchedule(PolicyScheduleEntity entity)
        {
            if (entity is null)
                return;

            Time = new DateTime(entity.TimeTicks);
            RepeatMode = (AlertRepeateMode)entity.RepeateMode;
        }


        internal void Update(PolicyScheduleUpdate update)
        {
            if (update is null)
                return;

            Time = update.Time ?? DateTime.MinValue;
            RepeatMode = update.RepeatMode;
        }


        internal DateTime GetSendTime()
        {
            if (RepeatMode == AlertRepeateMode.None)
                return Time;

            var shiftTime = RepeatMode switch
            {
                AlertRepeateMode.Hourly => TimeSpan.FromHours(1),
                AlertRepeateMode.Dayly => TimeSpan.FromDays(1),
                AlertRepeateMode.Weekly => TimeSpan.FromDays(7),
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