using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HSMCommon.Model;
using HSMServer.ApiObjectsConverters;
using HSMServer.Core.Model.HistoryValues;
using HSMServer.Model.History;
using Xunit;

namespace HSMServer.Core.Tests.Model
{
    // #1509: a bar's population StdDev, combined exactly when bars are compressed into coarser ones,
    // and carried (null = unknown) through the history outputs.
    public class BarStdDevTests
    {
        // {2,4,4,4} and {5,5,7,9}: the textbook series (mean 5, StdDev 2) split in two bars.
        private static readonly (int Count, double Mean, double? StdDev) FirstHalf = (4, 3.5, Math.Sqrt(0.75));
        private static readonly (int Count, double Mean, double? StdDev) SecondHalf = (4, 6.5, Math.Sqrt(2.75));

        private static readonly DateTime Start = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);


        [Fact]
        [Trait("Category", "Simple")]
        public void Combine_two_bars_gives_the_stddev_of_all_their_samples()
        {
            var combined = BarStdDev.Combine([FirstHalf, SecondHalf]);

            Assert.NotNull(combined);
            Assert.Equal(2.0, combined.Value, 12);
        }

        [Fact]
        [Trait("Category", "Simple")]
        public void Combine_weights_parts_by_count()
        {
            // {1} + {1,3,3}: all samples {1,1,3,3} -> mean 2, StdDev 1.
            var combined = BarStdDev.Combine([(1, 1.0, 0.0), (3, 7.0 / 3, Math.Sqrt(8.0 / 9))]);

            Assert.Equal(1.0, combined.Value, 12);
        }

        [Fact]
        [Trait("Category", "Simple")]
        public void Combine_of_one_counted_part_keeps_its_value()
        {
            Assert.Equal(1.41, BarStdDev.Combine([(5, 3.0, 1.41), (0, 0.0, null)]));
        }

        [Fact]
        [Trait("Category", "Simple")]
        public void Combine_is_unknown_when_any_counted_part_is_unknown()
        {
            Assert.Null(BarStdDev.Combine([FirstHalf, (4, 6.5, null)]));
        }

        [Fact]
        [Trait("Category", "Simple")]
        public void Combine_skips_parts_without_samples()
        {
            // A part with no samples changes nothing, whatever its StdDev.
            var combined = BarStdDev.Combine([FirstHalf, (0, 0.0, null), SecondHalf]);

            Assert.Equal(2.0, combined.Value, 12);
            Assert.Null(BarStdDev.Combine([(0, 0.0, null)]));
            Assert.Null(BarStdDev.Combine([]));
        }

        [Fact]
        [Trait("Category", "Simple")]
        public void History_compression_combines_the_stddev_of_the_merged_bars()
        {
            var bars = new List<BaseValue>
            {
                DoubleBar(0, FirstHalf),
                DoubleBar(1, SecondHalf),
            };

            var compressed = Compress(new DoubleBarHistoryProcessor(), bars, TimeSpan.FromMinutes(10));

            var bar = Assert.IsAssignableFrom<DoubleBarValue>(Assert.Single(compressed));
            Assert.Equal(5.0, bar.Mean, 12);
            Assert.Equal(2.0, bar.StdDev.Value, 12);
        }

        [Fact]
        [Trait("Category", "Simple")]
        public void History_compression_keeps_unknown_stddev_unknown()
        {
            var bars = new List<BaseValue>
            {
                DoubleBar(0, FirstHalf),
                DoubleBar(1, (4, 6.5, null)), // a bar from a collector that does not send StdDev
            };

            var compressed = Compress(new DoubleBarHistoryProcessor(), bars, TimeSpan.FromMinutes(10));

            Assert.Null(Assert.IsAssignableFrom<DoubleBarValue>(Assert.Single(compressed)).StdDev);
        }

        [Fact]
        [Trait("Category", "Simple")]
        public void History_compression_ignores_timeout_rows()
        {
            // GetTimeoutValue repeats the last bar with IsTimeout set; it must not weigh it twice.
            var last = DoubleBar(1, SecondHalf);
            var bars = new List<BaseValue>
            {
                DoubleBar(0, FirstHalf),
                last,
                last with { IsTimeout = true, Time = last.Time.AddMinutes(1) },
            };

            var compressed = Compress(new DoubleBarHistoryProcessor(), bars, TimeSpan.FromMinutes(10));

            Assert.Equal(2.0, Assert.IsAssignableFrom<DoubleBarValue>(Assert.Single(compressed)).StdDev.Value, 12);
        }

        [Fact]
        [Trait("Category", "Simple")]
        public void History_compression_of_int_bars_combines_from_the_stored_means()
        {
            var bars = new List<BaseValue>
            {
                new IntegerBarValue { Count = 2, Mean = 1, Min = 1, Max = 1, StdDev = 0, OpenTime = Start, CloseTime = Start.AddMinutes(1), Time = Start.AddMinutes(1) },
                new IntegerBarValue { Count = 2, Mean = 3, Min = 3, Max = 3, StdDev = 0, OpenTime = Start.AddMinutes(1), CloseTime = Start.AddMinutes(2), Time = Start.AddMinutes(2) },
            };

            var compressed = Compress(new IntBarHistoryProcessor(), bars, TimeSpan.FromMinutes(10));

            // {1,1,3,3} -> StdDev 1, reported as a double on an int bar.
            Assert.Equal(1.0, Assert.IsAssignableFrom<IntegerBarValue>(Assert.Single(compressed)).StdDev.Value, 12);
        }

        [Fact]
        [Trait("Category", "Simple")]
        public void Sensor_api_history_carries_stddev_and_null_when_unknown()
        {
            var known = (BarSensorHistory)new DoubleBarValue { Count = 4, Mean = 5, StdDev = 2 }.Convert();
            var unknown = (BarSensorHistory)new DoubleBarValue { Count = 4, Mean = 5 }.Convert();

            Assert.Equal(2.0.ToString(), known.StdDev);
            Assert.Null(unknown.StdDev);
        }


        private static DoubleBarValue DoubleBar(int minute, (int Count, double Mean, double? StdDev) part) =>
            new()
            {
                Count = part.Count,
                Mean = part.Mean,
                StdDev = part.StdDev,
                Min = part.Mean,
                Max = part.Mean,
                OpenTime = Start.AddMinutes(minute),
                CloseTime = Start.AddMinutes(minute + 1),
                Time = Start.AddMinutes(minute + 1),
            };

        // Compress is the processor's protected step (the public entry needs a whole sensor view model).
        private static List<BaseValue> Compress(HistoryProcessorBase processor, List<BaseValue> values, TimeSpan interval)
        {
            var compress = processor.GetType().GetMethod("Compress", BindingFlags.Instance | BindingFlags.NonPublic);

            return (List<BaseValue>)compress.Invoke(processor, [values, interval]);
        }
    }
}
