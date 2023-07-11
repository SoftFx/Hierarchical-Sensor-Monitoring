using HSMServer.Core.Model.Policies.Infrastructure;
using Microsoft.AspNetCore.Html;
using System.Linq;

namespace HSMServer.Helpers
{
    public static class DataAlertCommentHelper
    {
        public static HtmlString CreateCommentHelp() =>
            new($"There is the next variables:<br/>{string.Join("<br/>", CommentBuilder.Variables.Select(p => $"<b>{p.Key} -</b> {p.Value}"))}");
    }
}
