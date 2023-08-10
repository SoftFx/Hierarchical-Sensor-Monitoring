﻿using System;
using System.Collections.Concurrent;

namespace HSMServer.Notifications.Telegram.AddressBook.MessageBuilder
{
    internal sealed class GroupedPath
    {
        private const char Separator = '/';
        private const int MaxGroupedItemsCount = 9; //plus 1 main total 10

        private readonly ConcurrentQueue<string> _groupedNodes = new();
        private readonly string[] _templatePath;

        private int _totalGroupedItems = 1; //main item
        private int _groupedIndex = -1;


        internal GroupedPath(string path)
        {
            _templatePath = PathSplit(path);
        }


        internal bool Apply(string path)
        {
            var newPath = PathSplit(path);

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

        private static string[] PathSplit(string path) =>
            path?.Split(Separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            ?? Array.Empty<string>();
    }
}
