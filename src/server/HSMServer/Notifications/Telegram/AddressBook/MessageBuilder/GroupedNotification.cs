using HSMServer.Core.Model.Policies;
using System.Collections.Concurrent;

namespace HSMServer.Notifications.Telegram.AddressBook.MessageBuilder
{
    internal sealed class GroupedNotification
    {
        private const string FullCompare = "<Full equals>";
        private const int MaxGroupedItemsCount = 10;

        private readonly ConcurrentQueue<string> _groupedItems = new();

        private readonly AlertResult _baseAlert;
        private readonly AlertState _baseState;

        private string _groupDiffPropertyName;
        private int _totalGroupedItems = 1; //main item


        internal GroupedNotification(AlertResult alert)
        {
            _baseState = alert.LastState with { };
            _baseAlert = alert;
        }


        internal bool TryApply(AlertState alert)
        {
            if (_baseState is null)
                return false;

            var apply = _baseState.HasLessThanTwoDiff(alert, out var diffName);
            var isEmptyDiff = string.IsNullOrEmpty(diffName);

            if (isEmptyDiff && _totalGroupedItems == 1)
                _groupDiffPropertyName = FullCompare;

            apply &= (isEmptyDiff && _groupDiffPropertyName is FullCompare)
                     || diffName == _groupDiffPropertyName;

            if (apply)
            {
                _groupDiffPropertyName = diffName;
                _totalGroupedItems++;

                if (!isEmptyDiff)
                {
                    if (_groupedItems.IsEmpty)
                        _groupedItems.Enqueue(_baseState[diffName]);

                    if (_groupedItems.Count < MaxGroupedItemsCount)
                        _groupedItems.Enqueue(alert[diffName]);
                }
            }

            return apply;
        }


        public override string ToString()
        {
            if (_groupedItems.IsEmpty)
                return _baseAlert.BuildFullComment(_baseAlert.LastComment, _totalGroupedItems - 1); //remove main as extra
            else
            {
                var hiddenItemsCnt = _totalGroupedItems - _groupedItems.Count;

                var group = string.Join(", ", _groupedItems);

                if (hiddenItemsCnt > 0)
                    group = $"{group} ... and {hiddenItemsCnt} more";

                _baseState[_groupDiffPropertyName] = $"[{group}]";

                return _baseAlert.BuildFullComment(_baseState.BuildComment());
            }
        }
    }
}