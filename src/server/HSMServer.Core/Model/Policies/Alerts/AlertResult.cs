using HSMServer.Core.Extensions;
using System;
using System.Collections.Generic;
using System.Text;

namespace HSMServer.Core.Model.Policies
{
    public sealed record AlertDestination
    {
        public HashSet<Guid> Chats { get; init; }

        public bool AllChats { get; init; }


        public AlertDestination(Policy policy)
        {
            var target = policy.TargetChats;

            Chats = new HashSet<Guid>(target.Chats.Keys);
            AllChats = target.IsAllChats;
        }


        internal bool HasChats => AllChats || Chats.Count > 0;
    }


    public sealed record AlertResult
    {
        public AlertDestination Destination { get; }

        public Guid PolicyId { get; }


        public long? ConfirmationPeriod { get; }

        public DateTime BuildDate { get; }

        public DateTime SendTime { get; }


        public string Template { get; }

        public string Icon { get; }


        public AlertRepeatMode SchedulePeriod { get; }

        public bool ShouldSendFirstMessage { get; }


        public bool IsStatusIsChangeResult { get; }

        public bool IsScheduleAlert { get; }

        public bool IsReplaceAlert { get; }

        public bool IsValidAlert { get; }


        public AlertState LastState { get; private set; }

        public string LastComment { get; private set; }

        public int Count { get; private set; }


        public (string, int) Key => (Icon, Count);

        public int RetryCount { get; private set; }


        internal AlertResult(Policy policy, bool isReplace = false)
        {
            Destination = new(policy);
            
            ConfirmationPeriod = policy.ConfirmationPeriod;
            SendTime = policy.Schedule.GetSendTime();
            BuildDate = DateTime.UtcNow;

            ShouldSendFirstMessage = policy.Schedule.InstantSend;
            SchedulePeriod = policy.Schedule.RepeatMode;

            Template = policy.Template;
            PolicyId = policy.Id;
            Icon = policy.Icon;

            IsStatusIsChangeResult = policy.Conditions.IsStatusChangeResult();
            IsScheduleAlert = policy.UseScheduleManagerLogic;
            IsReplaceAlert = isReplace && IsScheduleAlert;
            IsValidAlert = Destination.HasChats && Template is not null;

            if (policy is TTLPolicy ttlPolicy)
                RetryCount = ttlPolicy.RetryCount;

            AddPolicyResult(policy);
        }


        public bool TryAddResult(AlertResult result)
        {
            if (PolicyId != result.PolicyId)
                return false;

            if (!TryCustomUpdateApply(result.LastState))
            {
                Count += result.Count;
                LastComment = result.LastComment;
                LastState = result.LastState;
            }

            return true;
        }

        internal void AddPolicyResult(Policy policy)
        {
            if (!TryCustomUpdateApply(policy.State))
            {
                Count++;
                LastComment = policy.Comment;
                LastState = policy.State;
            }
        }

        private bool TryCustomUpdateApply(AlertState newState)
        {
            if (IsStatusIsChangeResult && LastState is not null)
            {
                LastState = newState with
                {
                    PrevStatus = $"{LastState.PrevStatus}->{newState.PrevStatus}",
                    PrevComment = $"{LastState.PrevComment}->{newState.PrevComment}",
                    PrevValue = $"{LastState.PrevValue}->{newState.PrevValue}"
                };

                LastComment = LastState.BuildComment();

                return true;
            }

            return false;
        }

        public string BuildFullComment(string comment, int extraCnt = 0)
        {
            var sb = new StringBuilder(1 << 5);
            var totalCnt = Count + extraCnt;

            sb.Append($"{Icon} {comment}");

            if (totalCnt > 1)
                sb.Append($" ({totalCnt} times)");

            if (RetryCount > 0)
                sb.Append($" #{RetryCount}");

            return sb.ToString().Trim();
        }

        public override string ToString() => BuildFullComment(LastComment);
    }
}