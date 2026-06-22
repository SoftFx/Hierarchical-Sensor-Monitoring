using System;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace HSMServer.PathTemplates
{
    public sealed partial class PathTemplateConverter
    {
        // Matches a single path segment only: the charset intentionally excludes '/'.
        // The compiled regex is anchored as ^...$ against the sensor's FullPath, which is
        // product-prefixed (e.g. "ProductName/segment1/segment2"). To consume a product
        // name or any single segment, use '*'; to span multiple segments, chain '*' with
        // explicit '/' separators (e.g. "*/group/*/temperature"). '**' is NOT a glob-style
        // multi-segment wildcard — it produces two adjacent single-segment classes that
        // still cannot cross '/'.
        private const string AllValidSymbols = @"[\p{L}\p{Nd}\p{Zs}\._\#,%\$\-&]*";
        private const string NamedVariables = @"\{(.*?)\}";
        private const string UnnamedVariable = @"\*";

        private readonly ConcurrentDictionary<string, string> _namedVariables = new();
        private string _regexPattern;

        [GeneratedRegex(UnnamedVariable)]
        private static partial Regex EmptyVariableToRegex();

        [GeneratedRegex(NamedVariables)]
        private static partial Regex NamedVariableToRegex();

        [GeneratedRegex(@"[\{\}\(\)]+")]
        private static partial Regex ClearVariableName();

        [GeneratedRegex($@"[^{UnnamedVariable}{NamedVariables}]+")]
        private static partial Regex EscapeConstParts();


        public bool ApplyNewTemplate(string template, out string error)
        {
            _namedVariables.Clear();
            error = null;

            try
            {
                template = EscapeConstParts().Replace(template, "($0)"); // add () to const parts of template
                template = NamedVariableToRegex().Replace(template, g => $"(?<{RegisterNamedVariable(g.Value)}>{AllValidSymbols})"); // change custom variable {product} -> regex style (?<product>...)
                template = EmptyVariableToRegex().Replace(template, AllValidSymbols); //change noname variable * to regex pattern

                _regexPattern = $"^{template}$"; //set start and end string constants

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public bool IsMatch(string path)
        {
            if (string.IsNullOrEmpty(_regexPattern))
                return true;

            var match = Regex.Match(path, _regexPattern);

            if (match.Success)
            {
                foreach (var (variable, _) in _namedVariables)
                    _namedVariables[variable] = match.Groups[variable].Value;
            }

            return match.Success;
        }

        public string BuildStringByTempalte(string template) =>
            string.IsNullOrEmpty(template) ? string.Empty : NamedVariableToRegex().Replace(template, GetVariableByName);


        private string RegisterNamedVariable(string str)
        {
            var variable = ClearVariable(str);

            _namedVariables.TryAdd(variable, string.Empty);

            return variable;
        }

        private string GetVariableByName(Match match)
        {
            _namedVariables.TryGetValue(ClearVariable(match.Value), out var value);

            return value;
        }

        private static string ClearVariable(string value) => ClearVariableName().Replace(value, string.Empty).Trim();
    }
}