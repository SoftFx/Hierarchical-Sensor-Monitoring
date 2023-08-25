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


        private bool GroupByPath => _mainDiff == PathConst;


        internal GroupedNotification(AlertResult alert)
        {
            _baseState = alert.LastState with { };
            _baseAlert = alert;

            if (_baseState.Template.Contains(PathConst))
                _groupedPath = new GroupedPath(_baseState.Path);
        }


        internal bool TryApply(AlertState alert)
        {
            if (_baseState is null || _baseState.Template.Text != alert.Template.Text)
                return false;

            var apply = _baseState.HasLessThanTwoDiff(alert, out var diffName);
            var isEmptyDiff = string.IsNullOrEmpty(diffName);

            if (isEmptyDiff && _totalItems == 1)
                _mainDiff = FullCompare;

            if (apply && diffName == PathConst)
                apply &= _groupedPath.Apply(alert.Path);
            else
                apply &= IsEqualsTempaltes(diffName) || diffName == _mainDiff || string.IsNullOrEmpty(_mainDiff);

            if (apply)
            {
                _mainDiff = diffName;
                _totalItems++;

                if (!isEmptyDiff && !GroupByPath)
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
            if (!_groupedItems.IsEmpty || GroupByPath)
            {
                _baseState[_mainDiff] = GroupByPath ? $"{_groupedPath}" : BuildGroupedString(); ;

                return _baseAlert.BuildFullComment(_baseState.BuildComment());
            }

            return _baseAlert.BuildFullComment(_baseAlert.LastComment, _totalItems - 1); //remove main as extra
        }


        private bool IsEqualsTempaltes(string diff) => string.IsNullOrEmpty(diff) && _mainDiff is FullCompare;

        private string BuildGroupedString()
        {
            var hiddenItemsCnt = _totalItems - _groupedItems.Count;

            var group = string.Join(", ", _groupedItems);

            if (hiddenItemsCnt > 0)
                group = $"{group} ... and {hiddenItemsCnt} more";

            return $"[{group}]";
        }
    }
}