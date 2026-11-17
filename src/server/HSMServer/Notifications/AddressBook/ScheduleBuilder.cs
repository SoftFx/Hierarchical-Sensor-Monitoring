using HSMCommon.Extensions;
using HSMServer.Core.Extensions;
using HSMServer.Core.Model.Policies;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HSMServer.Notifications.AddressBook
{
    internal sealed class ScheduleBuilder : IMessageBuilder
    {
        private const string DayTempalte = "d MMMM (dddd)";

        private readonly Dictionary<AlertRepeatMode, Dictionary<DateTime, MessageBuilder>> _scheduleParts = new();
        private readonly object _partsLock = new();


        // Alerts arrive on the thread that raises them while the periodic flush builds the reports
        // on another one, so _scheduleParts is read and changed only under _partsLock (#1778).
        public void AddMessage(AlertResult alert)
        {
            var period = alert.SchedulePeriod;
            var groupPeriod = alert.BuildDate.Floor(period.ToTime());

            lock (_partsLock)
            {
                if (!_scheduleParts.TryGetValue(period, out var parts))
                    _scheduleParts[period] = parts = new();

                if (!parts.TryGetValue(groupPeriod, out var part))
                    parts[groupPeriod] = part = new();

                part.AddMessage(alert);
            }
        }

        // The caller sends every report before it asks for the next one, so alerts keep arriving
        // between two reports. A repeat mode leaves the buffer at the moment its report is built:
        // an alert is either in the parts that were taken or in the buffer for the next flush,
        // and a flush that stops early leaves the modes it has not reported in the buffer.
        internal IEnumerable<string> GetReports()
        {
            var sb = new StringBuilder(1 << 10);

            foreach (var mode in GetBufferedModes())
            {
                if (!TryTakeParts(mode, out var parts))
                    continue;

                var lastDate = new DateOnly();
                var period = mode.ToTime();

                sb.Clear();

                foreach (var (time, part) in parts.OrderBy(u => u.Key))
                {
                    var curDate = DateOnly.FromDateTime(time);

                    if (lastDate != curDate)
                    {
                        lastDate = curDate;
                        sb.AppendLine(curDate.ToString(DayTempalte));
                    }

                    var nextTime = time + period;

                    sb.AppendLine($"{time.Hour:00}:{time.Minute:00}-{nextTime.Hour:00}:{nextTime.Minute:00} (UTC)");
                    sb.AppendLine(part.GetAggregateMessage());
                }

                yield return sb.ToString();
            }
        }

        // Only the modes buffered when the flush starts: a mode that fills up again while its
        // report is being sent waits for the next flush, so one flush cannot run forever.
        private List<AlertRepeatMode> GetBufferedModes()
        {
            lock (_partsLock)
                return _scheduleParts.Keys.Order().ToList();
        }

        private bool TryTakeParts(AlertRepeatMode mode, out Dictionary<DateTime, MessageBuilder> parts)
        {
            lock (_partsLock)
                return _scheduleParts.Remove(mode, out parts);
        }
    }
}