using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.AspNetCore.Mvc.TagHelpers;
using System.Text.Encodings.Web;

namespace HSMServer.TagHelpers
{
    [HtmlTargetElement(Attributes = TagHelperName)]
    public class IsVisibleClassTagHelper : TagHelper
    {
        private const string TagHelperName = "is-visible";

        private const string HideElementClass = "d-none";
        private const string ShowElementClass = "d-flex";


        [HtmlAttributeName("")]
        public bool IsVisible { get; set; }


        public override void Process(TagHelperContext context, TagHelperOutput output)
        {
            if (IsVisible)
            {
                output.RemoveClass(HideElementClass, HtmlEncoder.Default);
                output.AddClass(ShowElementClass, HtmlEncoder.Default);
            }
            else
            {
                output.RemoveClass(ShowElementClass, HtmlEncoder.Default);
                output.AddClass(HideElementClass, HtmlEncoder.Default);
            }
        }
    }
}
