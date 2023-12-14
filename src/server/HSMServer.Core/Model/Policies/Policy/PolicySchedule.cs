using HSMCommon.Extensions;
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
    }


    public sealed class PolicySchedule
    {
        public DateTime Time { get; private set; }

        public AlertRepeateMode RepeateMode { get; private set; }


        internal PolicySchedule() { }

        public PolicySchedule(DateTime? time, AlertRepeateMode alertRepeatMode)
        {
            Time = time ?? DateTime.MinValue;
            RepeateMode = alertRepeatMode;
        }

        internal PolicySchedule(PolicyScheduleEntity entity)
        {
            if (entity is null)
                return;

            Time = new DateTime(entity.TimeTicks);
            RepeateMode = (AlertRepeateMode)entity.RepeateMode;
        }


        internal DateTime GetSendTime()
        {
            if (RepeateMode == AlertRepeateMode.None)
                return Time;

            var shiftTime = RepeateMode switch
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
                RepeateMode = (byte)RepeateMode,
            };
    }
}