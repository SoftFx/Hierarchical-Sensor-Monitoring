using System.Collections.Generic;
using HSMSensorDataObjects.BarData;
using HSMSensorDataObjects.FullDataObject;

namespace HSMServer.Core.Tests.MonitoringDataReceiverTests
{
    internal sealed class SensorValuesFactory
    {
        private readonly string _productKey;


        internal SensorValuesFactory(DatabaseAdapterManager dbManager) =>
            _productKey = dbManager.TestProduct.Key;


        internal SensorValueBase BuildRandomSensorValue() =>
            RandomValuesGenerator.GetRandomInt(min: 0, max: 8) switch
            {
                0 => BuildBoolSensorValue(),
                1 => BuildIntSensorValue(),
                2 => BuildDoubleSensorValue(),
                3 => BuildStringSensorValue(),
                4 => BuildIntBarSensorValue(),
                5 => BuildDoubleBarSensorValue(),
                6 => BuildFileSensorBytesValue(),
                7 => BuildFileSensorValue(),
                _ => null,
            };

        internal BoolSensorValue BuildBoolSensorValue()
        {
            var boolSensorValue = new BoolSensorValue()
            {
                BoolValue = RandomValuesGenerator.GetRandomBool(),
            };

            return boolSensorValue.FillCommonSensorValueProperties(_productKey);
        }

        internal IntSensorValue BuildIntSensorValue()
        {
            var intSensorValue = new IntSensorValue()
            {
                IntValue = RandomValuesGenerator.GetRandomInt(),
            };

            return intSensorValue.FillCommonSensorValueProperties(_productKey);
        }

        internal DoubleSensorValue BuildDoubleSensorValue()
        {
            var doubleSensorValue = new DoubleSensorValue()
            {
                DoubleValue = RandomValuesGenerator.GetRandomDouble(),
            };

            return doubleSensorValue.FillCommonSensorValueProperties(_productKey);
        }

        internal StringSensorValue BuildStringSensorValue()
        {
            var stringSensorValue = new StringSensorValue()
            {
                StringValue = RandomValuesGenerator.GetRandomString(),
            };

            return stringSensorValue.FillCommonSensorValueProperties(_productKey);
        }

        internal IntBarSensorValue BuildIntBarSensorValue()
        {
            var intBarSensorValue = new IntBarSensorValue()
            {
                LastValue = RandomValuesGenerator.GetRandomInt(),
                Min = RandomValuesGenerator.GetRandomInt(),
                Max = RandomValuesGenerator.GetRandomInt(),
                Mean = RandomValuesGenerator.GetRandomInt(),
                Percentiles = GetPercentileValuesInt(),
            };

            return intBarSensorValue.FillCommonBarSensorValueProperties(_productKey);
        }

        internal DoubleBarSensorValue BuildDoubleBarSensorValue()
        {
            var doubleBarSensorValue = new DoubleBarSensorValue()
            {
                LastValue = RandomValuesGenerator.GetRandomDouble(),
                Min = RandomValuesGenerator.GetRandomDouble(),
                Max = RandomValuesGenerator.GetRandomDouble(),
                Mean = RandomValuesGenerator.GetRandomDouble(),
                Percentiles = GetPercentileValuesDouble(),
            };

            return doubleBarSensorValue.FillCommonBarSensorValueProperties(_productKey);
        }

        internal FileSensorBytesValue BuildFileSensorBytesValue()
        {
            var fileSensorBytesValue = new FileSensorBytesValue()
            {
                Extension = RandomValuesGenerator.GetRandomString(3),
                FileContent = RandomValuesGenerator.GetRandomBytes(),
                FileName = nameof(FileSensorBytesValue),
            };

            return fileSensorBytesValue.FillCommonSensorValueProperties(_productKey);
        }

        internal FileSensorValue BuildFileSensorValue()
        {
            var fileSensorValue = new FileSensorValue()
            {
                Extension = RandomValuesGenerator.GetRandomString(3),
                FileContent = RandomValuesGenerator.GetRandomString(),
                FileName = nameof(FileSensorValue),
            };

            return fileSensorValue.FillCommonSensorValueProperties(_productKey);
        }

        private static List<PercentileValueInt> GetPercentileValuesInt(int size = 2)
        {
            var percentiles = new List<PercentileValueInt>(size);

            for (int i = 0; i < size; ++i)
                percentiles.Add(new PercentileValueInt(RandomValuesGenerator.GetRandomInt(), RandomValuesGenerator.GetRandomDouble()));

            return percentiles;
        }

        private static List<PercentileValueDouble> GetPercentileValuesDouble(int size = 2)
        {
            var percentiles = new List<PercentileValueDouble>(size);

            for (int i = 0; i < size; ++i)
                percentiles.Add(new PercentileValueDouble(RandomValuesGenerator.GetRandomDouble(), RandomValuesGenerator.GetRandomDouble()));

            return percentiles;
        }
    }
}
