using System.Collections.Generic;

namespace HSMServer.Core.Model.Policies
{
    public class AlertSystemTemplate : HashSet<string>
    {
        public string Text { get; init; }


        public AlertSystemTemplate() : base() { }

        public AlertSystemTemplate(IEnumerable<string> collection) : base(collection) { }
    }
}
