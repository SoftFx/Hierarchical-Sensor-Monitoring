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

        FiveMinutes = 5,
        TenMinutes = 6,
        FifteenMinutes = 7,

        ThirtyMinutes = 10,

        Hourly = 20,
        Daily = 50,
        Weekly = 100,
    }


    public sealed class PolicySchedule
    {
        public AlertRepeatMode RepeatMode { get; private set; }

        public bool InstantSend { get; private set; }

        public DateTime Time { get; private set; }

        public bool IsActive => RepeatMode is not AlertRepeatMode.Immediately;


        internal PolicySchedule() { }

        internal PolicySchedule(PolicyScheduleEntity entity)
        {
            if (entity is null)
                return;

            InstantSend = entity.InstantSend;
            Time = new DateTime(entity.TimeTicks);
            RepeatMode = (AlertRepeatMode)entity.RepeateMode;
        }


        internal void Update(PolicyScheduleUpdate update)
        {
            if (update is null)
                return;

            Time = update.Time ?? Time;
            InstantSend = update.InstantSend ?? InstantSend;
            RepeatMode = update.RepeatMode ?? RepeatMode;
        }


        internal DateTime GetSendTime()
        {
            if (RepeatMode == AlertRepeatMode.Immediately)
                return Time;

            var curTime = DateTime.UtcNow;

            return curTime <= Time ? Time : Time + (curTime - Time).Ceil(GetShiftTime());
        }

        internal TimeSpan GetShiftTime() => RepeatMode switch
        {
            AlertRepeatMode.FiveMinutes => TimeSpan.FromMinutes(5),
            AlertRepeatMode.TenMinutes => TimeSpan.FromMinutes(10),
            AlertRepeatMode.FifteenMinutes => TimeSpan.FromMinutes(15),
            AlertRepeatMode.ThirtyMinutes => TimeSpan.FromMinutes(30),
            AlertRepeatMode.Hourly => TimeSpan.FromHours(1),
            AlertRepeatMode.Daily => TimeSpan.FromDays(1),
            AlertRepeatMode.Weekly => TimeSpan.FromDays(7),
            _ => TimeSpan.Zero,
        };

        internal PolicyScheduleEntity ToEntity() =>
            new()
            {
                InstantSend = InstantSend,
                TimeTicks = Time.Ticks,
                RepeateMode = (byte)RepeatMode,
            };

        internal string ToTtlString() => IsActive ? $"scheduled {RepeatMode}" : string.Empty;

        public override string ToString() => IsActive
            ? $"scheduled {RepeatMode} starting at {Time.ToDefaultFormat()}{(InstantSend ? " and instant send" : string.Empty)}"
            : string.Empty;
    }
}