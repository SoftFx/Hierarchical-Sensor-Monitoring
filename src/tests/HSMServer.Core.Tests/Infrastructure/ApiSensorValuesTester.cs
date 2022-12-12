using HSMSensorDataObjects.FullDataObject;
using HSMServer.Core.Model;
using System;
using Xunit;
using SensorType = HSMSensorDataObjects.SensorType;

namespace HSMServer.Core.Tests.Infrastructure
{
    internal static class ApiSensorValuesTester
    {
        internal static void TestServerSensorValue(SensorValueBase expected, BaseValue actual)
        {
            Assert.NotEqual(DateTime.MinValue, actual.ReceivingTime);

            Assert.Equal(expected.Comment, actual.Comment);
            Assert.Equal(expected.Time, actual.Time);
            Assert.Equal(expected.Status.Convert(), actual.Status);
            Assert.Equal(expected.Type.Convert(), actual.Type);

            switch (expected)
            {
                case BoolSensorValue expectedBool:
                    TestSimpleValue(expectedBool, actual as BooleanValue);
                    break;
                case IntSensorValue expectedInt:
                    TestSimpleValue(expectedInt, actual as IntegerValue);
                    break;
                case DoubleSensorValue expectedDouble:
                    TestSimpleValue(expectedDouble, actual as DoubleValue);
                    break;
                case StringSensorValue expectedString:
                    TestSimpleValue(expectedString, actual as StringValue);
                    break;
                case FileSensorValue expectedFile:
                    TestFileValue(expectedFile, actual as FileValue);
                    break;
                case IntBarSensorValue expectedIntBar:
                    var actualIntBar = actual as IntegerBarValue;

                    TestBarValue(expectedIntBar, actualIntBar);
                    TestPercentiles(expectedIntBar, actualIntBar);

                    break;
                case DoubleBarSensorValue expectedDoubleBar:
                    var actualDoubleBar = actual as DoubleBarValue;

                    TestBarValue(expectedDoubleBar, actualDoubleBar);
                    TestPercentiles(expectedDoubleBar, actualDoubleBar);

                    break;
            }
        }

        private static void TestSimpleValue<T>(SensorValueBase<T> expected, BaseValue<T> actual) =>
            Assert.Equal(expected.Value, actual.Value);

        private static void TestFileValue(FileSensorValue expected, FileValue actual)
        {
            Assert.Equal(expected.Name, actual.Name);
            Assert.Equal(expected.Extension, actual.Extension);
            Assert.Equal(expected.Value.LongLength, actual.OriginalSize);

            TestSimpleValue(expected, actual);
        }

        private static void TestBarValue<T>(BarSensorValueBase<T> expected, BarBaseValue<T> actual) where T : struct
        {
            Assert.Equal(expected.Count, actual.Count);
            Assert.Equal(expected.OpenTime, actual.OpenTime);
            Assert.Equal(expected.CloseTime, actual.CloseTime);
            Assert.Equal(expected.Min, actual.Min);
            Assert.Equal(expected.Max, actual.Max);
            Assert.Equal(expected.Mean, actual.Mean);
            Assert.Equal(expected.LastValue, actual.LastValue);
        }

        private static void TestPercentiles(DoubleBarSensorValue expected, DoubleBarValue actual)
        {
            var expectedDict = expected.Percentiles ?? new();

            Assert.Equal(expectedDict, actual.Percentiles);
        }

        private static void TestPercentiles(IntBarSensorValue expected, IntegerBarValue actual)
        {
            var expectedDict = expected.Percentiles ?? new();

            Assert.Equal(expectedDict, actual.Percentiles);
        }


        private static Model.SensorType Convert(this SensorType type) =>
            type switch
            {
                SensorType.BooleanSensor => Model.SensorType.Boolean,
                SensorType.IntSensor => Model.SensorType.Integer,
                SensorType.DoubleSensor => Model.SensorType.Double,
                SensorType.StringSensor => Model.SensorType.String,
                SensorType.FileSensor => Model.SensorType.File,
                SensorType.IntegerBarSensor => Model.SensorType.IntegerBar,
                SensorType.DoubleBarSensor => Model.SensorType.DoubleBar,
                _ => throw new NotImplementedException(),
            };

        private static SensorStatus Convert(this HSMSensorDataObjects.SensorStatus status) =>
          status switch
          {
              HSMSensorDataObjects.SensorStatus.Ok => SensorStatus.Ok,
              HSMSensorDataObjects.SensorStatus.OffTime => SensorStatus.OffTime,
              HSMSensorDataObjects.SensorStatus.Error => SensorStatus.Error,
              HSMSensorDataObjects.SensorStatus.Warning => SensorStatus.Warning,
              _ => SensorStatus.Ok
          };
    }
}
