using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.AspNetCore.Mvc.TagHelpers;
using System.Text.Encodings.Web;

namespace HSMServer.TagHelpers
{
    [HtmlTargetElement(Attributes = TagHelperName)]
    public class IsVisibleClassTagHelper : TagHelper
    {
        private const string TagHelperName = "is-visible";

        [HtmlAttributeName("")]
        public bool IsVisible { get; set; }


        public override void Process(TagHelperContext context, TagHelperOutput output)
        {
            if (IsVisible)
            {
                output.RemoveClass("d-none", HtmlEncoder.Default);
                output.AddClass("d-flex", HtmlEncoder.Default);
            }
            else
            {
                output.RemoveClass("d-flex", HtmlEncoder.Default);
                output.AddClass("d-none", HtmlEncoder.Default);
            }
        }
    }
}
