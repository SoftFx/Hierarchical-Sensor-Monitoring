using System.Text;

namespace HSMServer.Core.Model.Policies
{
    public sealed class AlertResult
    {
        public string Icon { get; }

        public string Template { get; }


        public int Count { get; private set; } = 1;

        public string LastComment { get; private set; }


        internal AlertResult(Policy policy)
        {
            Icon = policy.Icon;
            Template = policy.Template;
            LastComment = policy.AlertComment;
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