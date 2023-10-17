using System;
using System.Collections.Generic;
using System.Text;

namespace HSMServer.Core.Model.Policies
{
    public sealed record AlertResult
    {
        public TimeIntervalModel Sensitivity { get; }

        public AlertDestination Destination { get; }

        public Guid PolicyId { get; }


        public string Template { get; }

        public string Icon { get; }


        public AlertState LastState { get; private set; }

        public string LastComment { get; private set; }

        public int Count { get; private set; }


        public (string, int) Key => (Icon, Count);


        internal AlertResult(Policy policy)
        {
            Destination = new(policy.Destination.AllChats, new HashSet<Guid>(policy.Destination.Chats.Keys));
            Sensitivity = policy.Sensitivity;
            Template = policy.Template;
            PolicyId = policy.Id;
            Icon = policy.Icon;

            AddPolicyResult(policy);
        }


        public bool TryAddResult(AlertResult alertResult)
        {
            if (PolicyId != alertResult.PolicyId)
                return false;

            Count += alertResult.Count;
            LastComment = alertResult.LastComment;
            LastState = alertResult.LastState;

            return true;
        }

        internal void AddPolicyResult(Policy policy)
        {
            Count++;
            LastComment = policy.Comment;
            LastState = policy.State;
        }


        public string BuildFullComment(string comment, int extraCnt = 0)
        {
            var sb = new StringBuilder(1 << 5);
            var totalCnt = Count + extraCnt;

            sb.Append($"{Icon} {comment}");

            if (totalCnt > 1)
                sb.Append($" ({totalCnt} times)");

            return sb.ToString().Trim();
        }

        public override string ToString() => BuildFullComment(LastComment);
    }


    public sealed record AlertDestination(bool AllChats, HashSet<Guid> Chats);
}