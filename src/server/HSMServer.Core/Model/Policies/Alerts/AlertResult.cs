﻿using System;
using System.Text;

namespace HSMServer.Core.Model.Policies
{
    public sealed record AlertResult
    {
        public string Icon { get; }

        public string Template { get; }

        public Guid PolicyId { get; }


        public AlertState LastState { get; private set; }

        public string LastComment { get; private set; }

        public int Count { get; private set; }


        public (string, int) Key => (Icon, Count);


        internal AlertResult(Policy policy)
        {
            Icon = policy.Icon;
            PolicyId = policy.Id;
            Template = policy.Template;

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

            sb.Append(Icon);

            if (totalCnt > 1)
                sb.Append($"({totalCnt} times)");

            return sb.Append($" {comment}")
                     .ToString();
        }

        public override string ToString() => BuildFullComment(LastComment);
    }
}