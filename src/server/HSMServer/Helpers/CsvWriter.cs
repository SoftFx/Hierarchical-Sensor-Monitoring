using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace HSMServer.Helpers
{
    internal sealed class CsvWriter
    {
        private readonly string _columnSeparator = CultureInfo.CurrentUICulture.TextInfo.ListSeparator;
        private readonly string _rowSeparator = Environment.NewLine;


        public List<string> Headers { get; set; }


        public CsvWriter(List<string> header) => Headers = header.ToList();


        public string GetHeaders() => string.Join(_columnSeparator, Headers);

        public string GetRow(Dictionary<string, object> values)
        {
            var row = new List<string>(values.Count);

            foreach (var header in Headers)
            {
                if (!values.TryGetValue(header, out var value))
                {
                    row.Add(string.Empty);
                    continue;
                }

                var valueStr = value?.ToString() ?? string.Empty;

                row.Add(valueStr.Contains(',') ? $"\"{valueStr}\"" : valueStr);
            }

            return string.Join(_columnSeparator, row);
        }

        public string GetContent(List<string> rows)
        {
            rows.Insert(0, GetHeaders());

            return string.Join(_rowSeparator, rows);
        }
    }
}
