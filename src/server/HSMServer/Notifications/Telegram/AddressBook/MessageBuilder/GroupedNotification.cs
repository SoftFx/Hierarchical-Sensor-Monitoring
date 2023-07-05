using HSMServer.Core.Model.Policies;
using System.Collections.Concurrent;

namespace HSMServer.Notifications.Telegram.AddressBook.MessageBuilder
{
    internal sealed class GroupedNotification
    {
        private const int MaxGroupedItemsCount = 9; //plus 1 main total 10
        internal const char Separator = '/';

        private readonly ConcurrentQueue<string> _groupedNodes = new();
        private readonly string[] _templatePath;

        private readonly AlertResult _baseAlert;

        private int _totalGroupedItems = 1; //main item
        private int _groupedIndex = -1;


        internal GroupedNotification(AlertResult alert)
        {
            _baseAlert = alert;
        }

        internal GroupedNotification(string[] path)
        {
            _templatePath = path;
        }


        internal bool TryApply(AlertResult alert)
        {
            return true;
        }


        internal bool Apply(string[] newPath)
        {
            if (_templatePath.Length != newPath.Length)
                return false;

            bool hasDiff = false;
            int diffIndex = -1;

            for (int i = 0; i < _templatePath.Length; ++i)
                if (_templatePath[i] != newPath[i])
                {
                    if (hasDiff)
                        return false;

                    hasDiff = true;
                    diffIndex = i;
                }

            if (hasDiff && (_groupedIndex == -1 || diffIndex == _groupedIndex))
            {
                if (_groupedNodes.Count < MaxGroupedItemsCount)
                    _groupedNodes.Enqueue(newPath[diffIndex]);

                _groupedIndex = diffIndex;
                _totalGroupedItems++;

                return true;
            }

            return false;
        }

        public override string ToString()
        {
            if (_groupedIndex != -1)
            {
                _groupedNodes.Enqueue(_templatePath[_groupedIndex]);

                var hiddenNodes = _totalGroupedItems - _groupedNodes.Count;
                var group = string.Join(", ", _groupedNodes);

                if (hiddenNodes > 0)
                    group = $"{group} ... and {hiddenNodes} more";

                _templatePath[_groupedIndex] = $"[{group}]";
            }

            return $"{Separator}{string.Join(Separator, _templatePath)}";
        }
    }
}
