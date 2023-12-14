using System;
using System.Collections.Concurrent;

namespace HSMServer.Notifications.Telegram.AddressBook
{
    internal sealed class ScheduleBuilder
    {
        private readonly ConcurrentDictionary<DateTime, MessageBuilder> _scheduleParts = new();

    }
}