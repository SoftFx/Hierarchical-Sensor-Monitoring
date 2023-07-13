using System;

namespace HSMServer.Core.Model.Policies
{
    [AttributeUsage(AttributeTargets.Property)]
    internal sealed class CommentVariableAttribute : Attribute
    {
        internal string Variable { get; }

        internal string Description { get; }


        public CommentVariableAttribute(string variable, string description)
        {
            Variable = variable;
            Description = description;
        }
    }
}
