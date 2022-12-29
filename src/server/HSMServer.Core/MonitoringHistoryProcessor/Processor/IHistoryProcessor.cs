﻿using HSMServer.Core.Model;
using System.Collections.Generic;

namespace HSMServer.Core.MonitoringHistoryProcessor.Processor
{
    public interface IHistoryProcessor
    {
        List<BaseValue> ProcessingAndCompression(List<BaseValue> values);

        string GetCsvHistory(List<BaseValue> originalData);
    }
}