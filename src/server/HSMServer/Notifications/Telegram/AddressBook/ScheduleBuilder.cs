using HSMCommon.Collections;
using HSMCommon.Extensions;
using HSMServer.Core.Extensions;
using HSMServer.Core.Model.Policies;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HSMServer.Notifications.Telegram.AddressBook
{
    internal sealed class ScheduleBuilder : IMessageBuilder
    {
        private const string DayTempalte = "d MMMM (dddd)";

        private readonly CDictBase<AlertRepeatMode, CTimeDict<MessageBuilder>> _scheduleParts = new();


        public void AddMessage(AlertResult alert)
        {
            var period = alert.SchedulePeriod;
            var groupPeriod = alert.BuildDate.Floor(period.ToTime());

            _scheduleParts[period][groupPeriod].AddMessage(alert);
        }

        internal IEnumerable<string> GetReports()
        {
            var sb = new StringBuilder(1 << 10);

            foreach (var (mode, builder) in _scheduleParts)
            {
                var lastDate = new DateOnly();
                var period = mode.ToTime();

                sb.Clear();

                foreach (var (time, part) in builder.OrderBy(u => u.Key).ToList())
                {
                    var curDate = DateOnly.FromDateTime(time);

                    if (lastDate != curDate)
                    {
                        lastDate = curDate;
                        sb.AppendLine(curDate.ToString(DayTempalte));
                    }

                    var nextTime = time + period;

                    sb.AppendLine($"{time.Hour}:{time.Minute}-{nextTime.Hour}:{nextTime.Minute} (UTC)");
                    sb.AppendLine(part.GetAggregateMessage());
                }

                _scheduleParts.Clear();

                yield return sb.ToString();
            }
        }
    }
}