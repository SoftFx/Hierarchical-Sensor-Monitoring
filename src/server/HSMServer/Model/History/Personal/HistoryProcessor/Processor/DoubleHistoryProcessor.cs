using HSMServer.Core.Model;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace HSMServer.Model.History
{
    internal sealed class DoubleHistoryProcessor : HistoryProcessorBase
    {
        private readonly NumberFormatInfo _format;


        public DoubleHistoryProcessor()
        {
            _format = new NumberFormatInfo { NumberDecimalSeparator = "." };
        }
    }
}
