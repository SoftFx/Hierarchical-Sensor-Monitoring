using System.Collections.Generic;
using System.Text;

namespace HSMServer.Model.MultiToastViewModels
{
    internal sealed class LimitedQueue<T> : Queue<T>
    {
        private readonly int _limit;

        private int _overflowCount = 0;


        public LimitedQueue(int limit) : base(limit)
        {
            _limit = limit;
        }

        public new void Enqueue(T item)
        {
            if (Count < _limit)
                base.Enqueue(item);
            else
                _overflowCount++;
        }

        public StringBuilder ToBuilder(StringBuilder builder, string header, string separator = ", ")
        {
            if (Count > 0)
                builder.AppendLine(header)
                       .AppendJoin(separator, ToArray())
                       .AppendLine();

            if (_overflowCount > 0)
                builder.AppendLine($"... and other {_overflowCount}");

            return builder;
        }
    }
}
