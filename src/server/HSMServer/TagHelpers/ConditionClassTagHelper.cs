using Microsoft.AspNetCore.Razor.TagHelpers;
using System.Collections.Generic;
using System;
using System.Linq;

namespace HSMServer.TagHelpers
{

    [HtmlTargetElement(Attributes = ClassPrefix + "*")]
    public class ConditionClassTagHelper : TagHelper
    {
        private const string ClassPrefix = "condition-class-";

        private IDictionary<string, bool> _classValues;

        [HtmlAttributeName("", DictionaryAttributePrefix = ClassPrefix)]
        public IDictionary<string, bool> ClassValues
        {
            get
            {
                return _classValues ??= new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            }
            set { _classValues = value; }
        }

        [HtmlAttributeName("class")]
        public string CssClass { get; set; }


        public override void Process(TagHelperContext context, TagHelperOutput output)
        {
            var items = _classValues.Where(e => e.Value).Select(e => e.Key).ToList();

            if (!string.IsNullOrEmpty(CssClass))
            {
                items.Insert(0, CssClass);
            }

            if (items.Count != 0)
            {
                var classes = string.Join(" ", [.. items]);
                output.Attributes.Add("class", classes);
            }
        }
    }
}
