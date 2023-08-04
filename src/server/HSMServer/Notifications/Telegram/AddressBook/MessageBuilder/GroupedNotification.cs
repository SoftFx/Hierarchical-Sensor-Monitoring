using HSMServer.Core.Model.Policies;
using System.Collections.Concurrent;

namespace HSMServer.Notifications.Telegram.AddressBook.MessageBuilder
{
    internal sealed class GroupedNotification
    {
        private const int MaxGroupedItemsCount = 10;

        private const string FullCompare = "<Full equals>";
        private const string PathConst = nameof(AlertState.Path);

        private readonly ConcurrentQueue<string> _groupedItems = new();

        private readonly GroupedPath _groupedPath;
        private readonly AlertResult _baseAlert;
        private readonly AlertState _baseState;

        private string _mainDiff;
        private int _totalItems = 1; //main item


        internal GroupedNotification(AlertResult alert)
        {
            _baseState = alert.LastState with { };
            _baseAlert = alert;

            if (_baseState.Template.Contains(PathConst))
                _groupedPath = new GroupedPath(_baseState.Path);
        }


        internal bool TryApply(AlertState alert)
        {
            if (_baseState is null)
                return false;

            var apply = _baseState.HasLessThanTwoDiff(alert, out var diffName);
            var isEmptyDiff = string.IsNullOrEmpty(diffName);

            if (isEmptyDiff && _totalItems == 1)
                _mainDiff = FullCompare;

            if (apply && diffName == PathConst)
                apply &= _groupedPath.Apply(diffName);
            else
                apply &= IsEqualsTempaltes(diffName) || diffName == _mainDiff || string.IsNullOrEmpty(_mainDiff);

            if (apply)
            {
                _mainDiff = diffName;
                _totalItems++;

                if (!isEmptyDiff && _mainDiff != PathConst)
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
                return _baseAlert.BuildFullComment(_baseAlert.LastComment, _totalItems - 1); //remove main as extra

            var group = _mainDiff == PathConst ? _groupedPath.ToString() : BuildGroupedString();

            _baseState[_mainDiff] = $"[{group}]";

            return _baseAlert.BuildFullComment(_baseState.BuildComment());
        }


        private bool IsEqualsTempaltes(string diff) => string.IsNullOrEmpty(diff) && _mainDiff is FullCompare;

        private string BuildGroupedString()
        {
            var hiddenItemsCnt = _totalItems - _groupedItems.Count;

            var group = string.Join(", ", _groupedItems);

            if (hiddenItemsCnt > 0)
                group = $"{group} ... and {hiddenItemsCnt} more";

            return group;
        }
    }
}