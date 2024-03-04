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

        public bool SendFirst { get; private set; }

        public DateTime Time { get; private set; }


        internal PolicySchedule() { }

        internal PolicySchedule(PolicyScheduleEntity entity)
        {
            if (entity is null)
                return;

            SendFirst = entity.SendFirst;
            Time = new DateTime(entity.TimeTicks);
            RepeatMode = (AlertRepeatMode)entity.RepeateMode;
        }


        internal void Update(PolicyScheduleUpdate update)
        {
            if (update is null)
                return;

            Time = update.Time ?? Time;
            SendFirst = update.InstantSend ?? SendFirst;
            RepeatMode = update.RepeatMode ?? RepeatMode;
        }


        internal DateTime GetSendTime()
        {
            if (RepeatMode == AlertRepeatMode.Immediately)
                return Time;

            var shiftTime = RepeatMode switch
            {
                AlertRepeatMode.FiveMinutes => TimeSpan.FromMinutes(5),
                AlertRepeatMode.TenMinutes => TimeSpan.FromMinutes(10),
                AlertRepeatMode.FifteenMinutes => TimeSpan.FromMinutes(15),
                AlertRepeatMode.ThirtyMinutes => TimeSpan.FromMinutes(30),
                AlertRepeatMode.Hourly => TimeSpan.FromHours(1),
                AlertRepeatMode.Daily => TimeSpan.FromDays(1),
                AlertRepeatMode.Weekly => TimeSpan.FromDays(7),
            };

            var curTime = DateTime.UtcNow;

            //shiftTime = TimeSpan.FromMinutes(5); //for test messages

            return curTime <= Time ? Time : Time + (curTime - Time).Ceil(shiftTime);
        }

        internal PolicyScheduleEntity ToEntity() =>
            new()
            {
                SendFirst = SendFirst,
                TimeTicks = Time.Ticks,
                RepeateMode = (byte)RepeatMode,
            };

        public override string ToString() => RepeatMode is AlertRepeatMode.Immediately
            ? string.Empty
            : $"scheduled {RepeatMode} starting at {Time.ToDefaultFormat()}{(SendFirst ? " and instant send" : string.Empty)}";
    }
}