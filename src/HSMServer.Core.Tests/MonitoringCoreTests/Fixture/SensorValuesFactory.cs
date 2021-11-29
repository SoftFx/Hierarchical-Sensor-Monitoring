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


        internal BoolSensorValue BuildBoolSensorValue()
        {
            var boolSensorValue = new BoolSensorValue()
            {
                BoolValue = RandomValues.GetRandomBool(),
            };

            return boolSensorValue.FillCommonSensorValueProperties(_productKey);
        }

        internal IntSensorValue BuildIntSensorValue()
        {
            var intSensorValue = new IntSensorValue()
            {
                IntValue = RandomValues.GetRandomInt(),
            };

            return intSensorValue.FillCommonSensorValueProperties(_productKey);
        }

        internal DoubleSensorValue BuildDoubleSensorValue()
        {
            var doubleSensorValue = new DoubleSensorValue()
            {
                DoubleValue = RandomValues.GetRandomDouble(),
            };

            return doubleSensorValue.FillCommonSensorValueProperties(_productKey);
        }

        internal StringSensorValue BuildStringSensorValue()
        {
            var stringSensorValue = new StringSensorValue()
            {
                StringValue = RandomValues.GetRandomString(),
            };

            return stringSensorValue.FillCommonSensorValueProperties(_productKey);
        }

        internal IntBarSensorValue BuildIntBarSensorValue()
        {
            var intBarSensorValue = new IntBarSensorValue()
            {
                LastValue = RandomValues.GetRandomInt(),
                Min = RandomValues.GetRandomInt(),
                Max = RandomValues.GetRandomInt(),
                Mean = RandomValues.GetRandomInt(),
                Percentiles = GetPercentileValuesInt(),
            };

            return intBarSensorValue.FillCommonBarSensorValueProperties(_productKey);
        }

        internal DoubleBarSensorValue BuildDoubleBarSensorValue()
        {
            var doubleBarSensorValue = new DoubleBarSensorValue()
            {
                LastValue = RandomValues.GetRandomDouble(),
                Min = RandomValues.GetRandomDouble(),
                Max = RandomValues.GetRandomDouble(),
                Mean = RandomValues.GetRandomDouble(),
                Percentiles = GetPercentileValuesDouble(),
            };

            return doubleBarSensorValue.FillCommonBarSensorValueProperties(_productKey);
        }

        internal FileSensorBytesValue BuildFileSensorBytesValue()
        {
            var fileSensorBytesValue = new FileSensorBytesValue()
            {
                Extension = RandomValues.GetRandomString(3),
                FileContent = RandomValues.GetRandomBytes(),
                FileName = nameof(FileSensorBytesValue),
            };

            return fileSensorBytesValue.FillCommonSensorValueProperties(_productKey);
        }

        internal FileSensorValue BuildFileSensorValue()
        {
            var fileSensorValue = new FileSensorValue()
            {
                Extension = RandomValues.GetRandomString(3),
                FileContent = RandomValues.GetRandomString(),
                FileName = nameof(FileSensorValue),
            };

            return fileSensorValue.FillCommonSensorValueProperties(_productKey);
        }

        private static List<PercentileValueInt> GetPercentileValuesInt(int size = 2)
        {
            var percentiles = new List<PercentileValueInt>(size);

            for (int i = 0; i < size; ++i)
                percentiles.Add(new PercentileValueInt(RandomValues.GetRandomInt(), RandomValues.GetRandomDouble()));

            return percentiles;
        }

        private static List<PercentileValueDouble> GetPercentileValuesDouble(int size = 2)
        {
            var percentiles = new List<PercentileValueDouble>(size);

            for (int i = 0; i < size; ++i)
                percentiles.Add(new PercentileValueDouble(RandomValues.GetRandomDouble(), RandomValues.GetRandomDouble()));

            return percentiles;
        }
    }
}
