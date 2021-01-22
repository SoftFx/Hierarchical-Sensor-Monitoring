using System;
using System.Collections.Generic;
using System.Text;

namespace HSMDataCollector.Bar
{
    public class BarCollectedEventArgs<T> : EventArgs
    {
        public int Count { get; set; }
        public string Type { get; set; }
        public T Min { get; set; }
        public T Max { get; set; }
        public T Mean { get; set; }
    }
}
