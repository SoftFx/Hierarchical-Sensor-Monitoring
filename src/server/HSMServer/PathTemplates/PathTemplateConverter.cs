using System;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace HSMServer.PathTemplates
{
    public sealed partial class PathTemplateConverter
    {
        private const string AllValidSymbols = @"[\p{L}\p{Nd}\p{Zs}]*";
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
                template = EscapeConstParts().Replace(template, "($0)"); // add () to const path of template
                template = NamedVariableToRegex().Replace(template, g => $"(?<{RegisterNamedVariable(g.Value)}>{AllValidSymbols})"); // change custom variable {product} -> regex style (?<product>...)
                template = EmptyVariableToRegex().Replace(template, AllValidSymbols); //change noname variable to valid pattern

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
            var match = Regex.Match(path, _regexPattern);

            if (match.Success)
            {
                foreach (var (variable, _) in _namedVariables)
                    _namedVariables[variable] = match.Groups[variable].Value;
            }

            return match.Success;
        }

        public string BuildStringByTempalte(string template)
        {
            return template;
        }

        private string RegisterNamedVariable(string str)
        {
            var variable = ClearVariableName().Replace(str, string.Empty).Trim();

            _namedVariables.TryAdd(variable, string.Empty);

            return variable;
        }
    }
}