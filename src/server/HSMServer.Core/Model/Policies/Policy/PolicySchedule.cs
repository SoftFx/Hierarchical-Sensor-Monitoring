using HSMCommon.Extensions;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Extensions;
using System;

namespace HSMServer.Core.Model.Policies
{
    public enum AlertRepeatMode : byte
    {
        Immediately = 0,

        Hourly = 20,
        Daily = 50,
        Weekly = 100,
    }


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
            if (RepeatMode == AlertRepeatMode.Immediately)
                return Time;

            var shiftTime = RepeatMode switch
            {
                AlertRepeatMode.Hourly => TimeSpan.FromHours(1),
                AlertRepeatMode.Daily => TimeSpan.FromDays(1),
                AlertRepeatMode.Weekly => TimeSpan.FromDays(7),
            };

            var curTime = DateTime.UtcNow;

            //shiftTime = TimeSpan.FromMinutes(5);

            return curTime <= Time ? Time : Time + (curTime - Time).Ceil(shiftTime);
        }

        internal PolicyScheduleEntity ToEntity() =>
            new()
            {
                TimeTicks = Time.Ticks,
                RepeateMode = (byte)RepeatMode,
            };

        public override string ToString() => RepeatMode is AlertRepeatMode.Immediately
            ? string.Empty
            : $"scheduled {RepeatMode} starting at {Time.ToDefaultFormat()}";
    }
}