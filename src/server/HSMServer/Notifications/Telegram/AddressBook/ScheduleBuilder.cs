using HSMCommon.Collections;
using HSMCommon.Extensions;
using HSMServer.Core.Model.Policies;
using System;
using System.Linq;
using System.Text;

namespace HSMServer.Notifications.Telegram.AddressBook
{
    internal sealed class ScheduleBuilder : IMessageBuilder
    {
        private const string DayTempalte = "d MMMM (dddd)";

        private readonly CTimeDict<MessageBuilder> _scheduleParts = new();
        private readonly TimeSpan _grouppingPeriod = TimeSpan.FromMinutes(5);


        public void AddMessage(AlertResult alert)
        {
            var grouppingDate = alert.BuildDate.Floor(_grouppingPeriod);

            _scheduleParts[grouppingDate].AddMessage(alert);
        }

        internal string GetReport()
        {
            var sb = new StringBuilder(1 << 10);
            var lastDate = new DateOnly();

            foreach (var (time, part) in _scheduleParts.OrderBy(u => u.Key).ToList())
            {
                var curDate = DateOnly.FromDateTime(time);

                if (lastDate != curDate)
                {
                    lastDate = curDate;
                    sb.AppendLine(curDate.ToString(DayTempalte));
                }

                var nextTime = time + _grouppingPeriod;

                sb.AppendLine($"{time.Hour}:{time.Minute}-{nextTime.Hour}:{nextTime.Minute} (UTC)");
                sb.AppendLine(part.GetAggregateMessage());
            }

            _scheduleParts.Clear();

            return sb.ToString();
        }
    }
}