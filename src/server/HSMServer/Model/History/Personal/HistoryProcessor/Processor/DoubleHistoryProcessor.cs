﻿using System.Globalization;

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
