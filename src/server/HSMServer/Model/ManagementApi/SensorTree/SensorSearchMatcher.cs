using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using HSMServer.Core.Model;

namespace HSMServer.Model.ManagementApi.SensorTree
{
    // Builds the sensor search predicate of GET /api/v1/sensors (#1386). The search
    // text matches DisplayName, Description and Path (OR) of a sensor:
    //   - contains (default): case-insensitive Ordinal substring;
    //   - regex:               case-insensitive .NET regular expression.
    // Both bounds are enforced BEFORE any cache enumeration: a pattern longer than
    // MaxSearchLength is a 400, and the compiled regex carries a match timeout so a
    // catastrophic pattern can never hang a request (the timeout raises
    // RegexMatchTimeoutException from the filter pass — the controller maps it to a
    // 400 as well).
    public static class SensorSearchMatcher
    {
        public const string ContainsMode = "contains";
        public const string RegexMode = "regex";

        public const int MaxSearchLength = 200;
        public const int RegexTimeoutMs = 100;


        // A null/empty search matches everything — the endpoint doubles as a plain
        // sensor listing. Errors are field-keyed for the uniform validation body.
        public static bool TryBuild(string search, string searchMode,
            out Func<BaseSensorModel, bool> predicate, out IDictionary<string, string[]> errors)
        {
            predicate = MatchesEverything;
            errors = null;

            if (string.IsNullOrEmpty(search))
                return true;

            var mode = ResolveMode(searchMode);
            if (mode is null)
            {
                errors = NewError($"Unknown search mode '{searchMode}'. Valid values: {ContainsMode}, {RegexMode}.");
                return false;
            }

            if (search.Length > MaxSearchLength)
            {
                errors = NewError($"The search text is longer than {MaxSearchLength} characters.");
                return false;
            }

            if (mode == ContainsMode)
            {
                predicate = sensor => Contains(sensor.DisplayName) || Contains(sensor.Description) || Contains(sensor.FullPath);
                return true;
            }

            if (!TryCompileRegex(search, out var regex, out var error))
            {
                errors = NewError(error);
                return false;
            }

            predicate = sensor => regex.IsMatch(sensor.DisplayName) || regex.IsMatch(sensor.Description) || regex.IsMatch(sensor.FullPath);
            return true;


            bool Contains(string field) =>
                !string.IsNullOrEmpty(field) && field.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
        }


        // Compiles the agent-supplied pattern with the match timeout bound. Argument
        // exceptions cover the invalid-pattern grammar; the length bound above keeps
        // the compiled pattern itself sane.
        public static bool TryCompileRegex(string pattern, out Regex regex, out string error)
        {
            try
            {
                regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                    matchTimeout: TimeSpan.FromMilliseconds(RegexTimeoutMs));
                error = null;
                return true;
            }
            catch (ArgumentException)
            {
                regex = null;
                error = "The search pattern is not a valid .NET regular expression.";
                return false;
            }
        }


        private static string ResolveMode(string searchMode)
        {
            if (string.IsNullOrEmpty(searchMode) || string.Equals(searchMode, ContainsMode, StringComparison.OrdinalIgnoreCase))
                return ContainsMode;

            if (string.Equals(searchMode, RegexMode, StringComparison.OrdinalIgnoreCase))
                return RegexMode;

            return null;
        }


        private static IDictionary<string, string[]> NewError(string message) =>
            new Dictionary<string, string[]> { ["search"] = [message] };


        private static bool MatchesEverything(BaseSensorModel sensor) => true;
    }
}
