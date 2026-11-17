using System;
using HSMServer.Core.Model.Policies;

namespace HSMServer.Core.Tests.Notifications
{
    // Alerts reach a chat's buffer on the thread that raises the alert, while the periodic flush
    // drains the buffer on another one. A test cannot schedule two threads, so it puts the second
    // thread's step inside the drain instead: the drain copies the state of every alert it groups
    // (GroupedNotification), and this state runs a callback when it is copied. The callback is
    // "the other thread", at a point of the drain the test knows exactly.
    internal sealed record DrainInterleavingState : AlertState
    {
        public Action Grouping { get; init; }


        public DrainInterleavingState() { }

        private DrainInterleavingState(DrainInterleavingState original) : base(original)
        {
            Grouping = original.Grouping;
            Grouping?.Invoke();
        }
    }
}
