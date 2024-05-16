using System;
using System.Linq;
using System.Reflection;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Mvc.TagHelpers;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using HSMCommon.Extensions;
using HSMServer.Core.Attributes;


namespace HSMServer.TagHelpers
{
    [HtmlTargetElement("enum-selectpicker")]
    public class EnumSelectpickerTagHelper : TagHelper
    {
        private struct Option
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public int GroupPriority { get; set; }
            public string GroupName { get; set; }
            public int NumberInGroup { get; set; }
        }

        private static readonly ConcurrentDictionary<Type, List<Option>> _cache = new();

        [HtmlAttributeName("asp-for")]
        public ModelExpression AspFor { get; set; }


        public override void Process(TagHelperContext context, TagHelperOutput output)
        {
            var options = ParseEnum();

            output.TagName = "select";
            output.TagMode = TagMode.StartTagAndEndTag;
            output.AddClass("selectpicker", HtmlEncoder.Default);
            output.Attributes.SetAttribute("id", AspFor.Metadata.Name);
            output.Attributes.SetAttribute("name", AspFor.Metadata.Name);

            StringBuilder sb = new();

            string currentGroup = null;

            if (AspFor.Model is null)
                sb.Append("<option value=\"\" disabled selected hidden></option>");

            foreach (var option in options)
            {
                if (currentGroup == null || currentGroup != option.GroupName)
                {
                    if (currentGroup != null)
                        sb.Append("<option data-divider=\"true\"></option>");

                    if (option.GroupName?.Length != 0)
                        sb.Append($"<option disabled>{option.GroupName}</option>");

                    currentGroup = option.GroupName;
                }

                sb.Append($"<option value={option.Id} {(option.Id == AspFor.Model?.ToString() ? "selected" : "")}>{option.Name}</option>");
            }

            output.PreContent.SetHtmlContent(sb.ToString());
        }

        private List<Option> ParseEnum()
        {
            var type = AspFor.ModelExplorer.ModelType.GenericTypeArguments[0];

            if (_cache.TryGetValue(type, out List<Option> result))
                return result;

            result = GetEnumValues(type).OrderBy(x => x.GroupPriority).ThenBy(x => x.GroupName).ThenBy(x => x.NumberInGroup).ToList();

            _cache.TryAdd(type, result);

            return result;
        }

        private static IEnumerable<Option> GetEnumValues(Type type)
        {
            foreach (Enum value in Enum.GetValues(type))
            {
                var enumValue = value.ToString();
                GroupAttribute attr = type.GetMember(enumValue).First().GetCustomAttribute<GroupAttribute>();

                yield return new Option
                {
                    Id = enumValue,
                    Name = value.GetDisplayName(),
                    GroupPriority = attr?.Priority ?? 0,
                    GroupName = attr?.GroupName ?? string.Empty,
                    NumberInGroup = attr?.NumberInGroup ?? 0
                };
            }
        }
    }
}
