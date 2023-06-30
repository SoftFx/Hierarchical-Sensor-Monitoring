using System;
using System.Text;

namespace HSMServer.Core.Model.Policies
{
    public sealed class AlertResult
    {
        public string Icon { get; }

        public string Template { get; }

        public Guid PolicyId { get; }


        public int Count { get; private set; } = 1;

        public string LastComment { get; private set; }


        internal AlertResult(Policy policy)
        {
            Icon = policy.Icon;
            PolicyId = policy.Id;
            Template = policy.Template;
            LastComment = policy.AlertComment;
        }


        public bool TryAddResult(AlertResult alertResult)
        {
            if (PolicyId != alertResult.PolicyId)
                return false;

            Count += alertResult.Count;
            LastComment = alertResult.LastComment;

            return true;
        }

        internal void AddComment(string comment)
        {
            Count++;
            LastComment = comment;
        }

        public override string ToString()
        {
            var sb = new StringBuilder(1 << 5);

            sb.Append(Icon);

            if (Count > 1)
                sb.Append($"({Count})");

            sb.Append($" {LastComment}");

            return sb.ToString();
        }
    }
}