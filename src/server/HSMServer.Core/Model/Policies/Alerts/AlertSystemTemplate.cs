using System.Collections.Generic;

namespace HSMServer.Core.Model.Policies
{
    public record AlertSystemTemplate
    {
        public HashSet<string> UsedVariables { get; init; }

        public string Template { get; init; }
    }
}
