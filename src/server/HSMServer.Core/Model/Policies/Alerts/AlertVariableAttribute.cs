using System;

namespace HSMServer.Core.Model.Policies
{
    [AttributeUsage(AttributeTargets.Property)]
    internal sealed class AlertVariableAttribute : Attribute
    {
        internal string Variable { get; }

        internal string Description { get; }


        public AlertVariableAttribute(string variable, string description)
        {
            Variable = variable;
            Description = description;
        }
    }
}