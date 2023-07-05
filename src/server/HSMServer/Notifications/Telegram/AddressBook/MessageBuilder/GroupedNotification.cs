using HSMServer.Core.Model.Policies;
using System.Collections.Concurrent;

namespace HSMServer.Notifications.Telegram.AddressBook.MessageBuilder
{
    internal sealed class GroupedNotification
    {
        private const int MaxGroupedItemsCount = 10;

        private readonly ConcurrentQueue<string> _groupedItems = new();

        private readonly AlertResult _baseAlert;
        private readonly AlertState _baseState;

        private string _groupDiffPropertyName;
        private int _totalGroupedItems = 1; //main item


        internal GroupedNotification(AlertResult alert)
        {
            _baseState = alert.LastState;
            _baseAlert = alert;
        }


        internal bool TryApply(AlertState alert)
        {
            var apply = _baseState.HasLessThanTwoDiff(alert, out var diffName);

            apply &= string.IsNullOrEmpty(diffName) || diffName == _groupDiffPropertyName;

            if (apply)
            {
                if (_groupedItems.IsEmpty)
                    _groupedItems.Enqueue(_baseState[diffName]);

                if (_groupedItems.Count < MaxGroupedItemsCount)
                    _groupedItems.Enqueue(alert[diffName]);

                _groupDiffPropertyName = diffName;
                _totalGroupedItems++;
            }

            return apply;
        }


        public override string ToString()
        {
            if (_groupedItems.IsEmpty)
                return _baseAlert.ToString();
            else
            {
                var hiddenItemsCnt = _totalGroupedItems - _groupedItems.Count;

                var group = string.Join(", ", _groupedItems);

                if (hiddenItemsCnt > 0)
                    group = $"{group} ... and {hiddenItemsCnt} more";

                return _baseAlert.BuildFullComment($"[{group}]");
            }
        }
    }
}