using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.SensorsUpdatesQueue;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using HSMServer.Core.Tests.TreeValuesCacheTests.Fixture;
using System;
using Xunit;
using SensorModelFactory = HSMServer.Core.Tests.Infrastructure.SensorModelFactory;

namespace HSMServer.Core.Tests.TreeValuesCacheTests
{
    public class BaseSensorModelValidatorTests : MonitoringCoreTestsBase<ValidationFixture>
    {
        private const string SensorValueIsTooLong = "The value has exceeded the length limit.";
        private const string SensorValueOutdated = "";
        private const string SensorValueTypeInvalid = "Sensor value type is not {0}";
        private const string SensorValueStatusInvalid = "User data has {0} status";

        private const string ErrorPathKey = "Path or key is empty.";
        private const string ErrorTooLongPath = "Path for the sensor is too long.";
        private const string ErrorInvalidPath = "Path has an invalid format.";
        private const string ErrorKeyNotFound = "Key doesn't exist.";
        private const string ErrorHaventRule = "AccessKey doesn't have CanSendSensorData.";

        private const string InvalidTooLongPath = "a/a/a/a/a/a/a/a/a/a/a";
        private const string ValidPath = "a/a/a/a/a";

        private const int DefaultMaxStringLength = 150;
        private const int TestTicks = 50000;


        public BaseSensorModelValidatorTests(ValidationFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture, addTestProduct: false) { }


        [Theory]
        [InlineData(SensorType.Boolean)]
        [InlineData(SensorType.Integer)]
        [InlineData(SensorType.Double)]
        [InlineData(SensorType.String)]
        [InlineData(SensorType.IntegerBar)]
        [InlineData(SensorType.DoubleBar)]
        [InlineData(SensorType.File)]
        [Trait("Category", "NullBase")]
        public void NullSensorValueValidationTest(SensorType type)
        {
            var sensor = BuildSensorModel(type);

            Assert.False(sensor.TryAddValue((BaseValue)null));
        }

        [Fact]
        [Trait("Category", "StringSensorValueTooLong")]
        public void StringSensorValueTooLongValidationTest()
        {
            var sensor = BuildSensorModel(SensorType.String);
            sensor.AddPolicy(new StringValueLengthPolicy());

            var stringBase = new StringValue
            {
                Value = RandomGenerator.GetRandomString(DefaultMaxStringLength + 1)
            };

            Assert.True(sensor.TryAddValue(stringBase));
            Assert.True(sensor.ValidationResult.HasWarning);
            Assert.Equal(SensorStatus.Warning, sensor.ValidationResult.Status);
            Assert.Equal(sensor.ValidationResult.Message, SensorValueIsTooLong);
        }

        [Theory]
        [InlineData(SensorType.Boolean)]
        [InlineData(SensorType.Integer)]
        [InlineData(SensorType.Double)]
        [InlineData(SensorType.String)]
        [InlineData(SensorType.IntegerBar)]
        [InlineData(SensorType.DoubleBar)]
        [InlineData(SensorType.File)]
        [Trait("Category", "InvalidType")]
        public void SensorModelInvalidTypeValidationTest(SensorType sensorType)
        {
            var sensor = BuildSensorModel(sensorType);
            var errorMessage = GetBadValueTypeMessage(sensorType);

            foreach (var invalidType in Enum.GetValues<SensorType>())
            {
                if (invalidType == sensorType)
                    break;

                Assert.False(sensor.TryAddValue(SensorValuesFactory.BuildSensorValue(invalidType)));
                Assert.True(sensor.ValidationResult.HasError);
                Assert.Equal(SensorStatus.Error, sensor.ValidationResult.Status);
                Assert.Equal(errorMessage, sensor.ValidationResult.Message);
            }
        }

        [Theory]
        [InlineData(SensorStatus.OffTime)]
        [InlineData(SensorStatus.Error)]
        [InlineData(SensorStatus.Warning)]
        [Trait("Category", "InvalidStatus")]
        public void SensorValueStatusValidationTest(SensorStatus status)
        {
            foreach (var sensorType in Enum.GetValues<SensorType>())
            {
                var sensor = BuildSensorModel(sensorType);
                var baseValue = SensorValuesFactory.BuildSensorValue(sensorType) with { Status = status };
                var expectedMessage = string.IsNullOrEmpty(baseValue.Comment)
                    ? string.Format(SensorValueStatusInvalid, status)
                    : baseValue.Comment;

                Assert.True(sensor.TryAddValue(baseValue));
                Assert.Equal(baseValue.Status, sensor.ValidationResult.Status);
                Assert.Equal(expectedMessage, sensor.ValidationResult.Message);

                if (status == SensorStatus.Error)
                    Assert.True(sensor.ValidationResult.HasError);
                else if (status == SensorStatus.Warning)
                    Assert.True(sensor.ValidationResult.HasWarning);
            }
        }

        [Theory]
        [InlineData(1000)]
        [InlineData(2000)]
        [InlineData(5000)]
        [InlineData(10000)]
        [InlineData(50000)]
        [InlineData(100000)]
        [Trait("Catgeory", "UpdateIntervalPolicy")]
        public void SensorModelUpdateIntervalValidationTest(long ticks)
        {
            foreach (var sensorType in Enum.GetValues<SensorType>())
            {
                var sensor = BuildSensorModel(sensorType);
                sensor.AddPolicy(new ExpectedUpdateIntervalPolicy(ticks));

                var baseValue = SensorValuesFactory.BuildSensorValue(sensorType) with
                { ReceivingTime = new DateTime(DateTime.UtcNow.Ticks - ticks) };

                Assert.True(sensor.TryAddValue(baseValue));
                Assert.True(sensor.HasUpdateTimeout());
                Assert.True(sensor.ValidationResult.HasWarning);
                Assert.Equal(SensorStatus.Warning, sensor.ValidationResult.Status);
                Assert.Equal(SensorValueOutdated, sensor.ValidationResult.Message);
            }
        }

        [Theory]
        [InlineData(SensorStatus.OffTime)]
        [InlineData(SensorStatus.Error)]
        [InlineData(SensorStatus.Warning)]
        [Trait("Category", "CombinatedStatusWithTooLongLength")]
        public void CombinatedStatusWithTooLongLenghtValidationTest(SensorStatus status)
        {
            var sensor = BuildSensorModel(SensorType.String);
            sensor.AddPolicy(new StringValueLengthPolicy());

            var stringBase = new StringValue
            {
                Value = RandomGenerator.GetRandomString(DefaultMaxStringLength + 1),
                Status = status
            };

            Assert.True(sensor.TryAddValue(stringBase));
            Assert.True(sensor.ValidationResult.HasWarning);
            Assert.Equal(GetFinalStatus(status, SensorStatus.Warning), sensor.ValidationResult.Status);

            if (status == SensorStatus.Error)
                Assert.True(sensor.ValidationResult.HasError);
        }

        [Theory]
        [InlineData(SensorStatus.OffTime)]
        [InlineData(SensorStatus.Error)]
        [InlineData(SensorStatus.Warning)]
        [Trait("Cetagory", "CombinatedStatusWithInterval")]
        public void CombinatedStatusWithIntervalValidationTest(SensorStatus status)
        {
            foreach (var sensorType in Enum.GetValues<SensorType>())
            {
                var sensor = BuildSensorModel(sensorType);
                sensor.AddPolicy(new ExpectedUpdateIntervalPolicy(TestTicks));

                var baseValue = SensorValuesFactory.BuildSensorValue(sensorType) with
                {
                    ReceivingTime = new DateTime(DateTime.UtcNow.Ticks - TestTicks),
                    Status = status
                };

                Assert.True(sensor.TryAddValue(baseValue));
                Assert.True(sensor.HasUpdateTimeout());
                Assert.True(sensor.ValidationResult.HasWarning);
                Assert.Equal(GetFinalStatus(status, SensorStatus.Warning), sensor.ValidationResult.Status);

                if (status == SensorStatus.Error)
                    Assert.True(sensor.ValidationResult.HasError);
            }
        }

        [Fact]
        [Trait("Category", "EmptyPathOrKey")]
        public void EmptyPathOrKeyValidationTest()
        {
            var info = new StoreInfo(string.Empty, string.Empty);

            Assert.False(info.TryCheckRequest(out var message));
            Assert.Equal(ErrorPathKey, message);
        }

        [Fact]
        [Trait("Category", "TooLongPath")]
        public void TooLongPathValidationTest()
        {
            var info = new StoreInfo(Guid.NewGuid().ToString(), InvalidTooLongPath);

            Assert.False(info.TryCheckRequest(out var message));
            Assert.Equal(ErrorTooLongPath, message);
        }

        [Theory]
        [InlineData("/")]
        [InlineData("///")]
        [InlineData("a//")]
        [InlineData("/a/")]
        [InlineData("//a")]
        [InlineData("  /  ")]
        [InlineData("a/  ")]
        [InlineData("a a a/ ")]
        [InlineData("a/ /  ")]
        [InlineData("a / a / ")]
        [Trait("Category", "InvalidPath")]
        public void InvalidPathValidationTest(string path)
        {
            var info = new StoreInfo(Guid.NewGuid().ToString(), path);

            Assert.False(info.TryCheckRequest(out var message));
            Assert.Equal(ErrorInvalidPath, message);
        }

        [Fact]
        [Trait("Category", "InvalidKey")]
        public void InvalidKeyValidationTest()
        {
            var info = new StoreInfo(Guid.NewGuid().ToString(), ValidPath);

            Assert.False(_valuesCache.TryCheckKeyWritePermissions(info, out var message));
            Assert.Equal(ErrorKeyNotFound, message);
        }

        [Theory]
        [InlineData(KeyPermissions.CanAddNodes)]
        [InlineData(KeyPermissions.CanAddSensors)]
        [Trait("Category", "SmallRules")]
        public void SmallRulesValidationTest(KeyPermissions permission)
        {
            var product = _valuesCache.AddProduct(TestProductsManager.ProductName);
            var accessKey = new AccessKeyModel(EntitiesFactory.BuildAccessKeyEntity(productId: product.Id.ToString(), permissions: permission));

            _valuesCache.AddAccessKey(accessKey);

            var info = new StoreInfo(accessKey.Id.ToString(), ValidPath);

            Assert.False(_valuesCache.TryCheckKeyWritePermissions(info, out var message));
            Assert.Equal(ErrorHaventRule, message);
        }


        private static BaseSensorModel BuildSensorModel(SensorType type)
        {
            var entity = new SensorEntity()
            {
                Id = Guid.NewGuid().ToString(),
                ProductId = Guid.NewGuid().ToString(),
                DisplayName = RandomGenerator.GetRandomString(),
                Type = (byte)type
            };

            return SensorModelFactory.Build(entity);
        }

        private static string GetBadValueTypeMessage(SensorType sensorType)
        {
            var name = sensorType switch
            {
                SensorType.Boolean => nameof(BooleanValue),
                SensorType.Integer => nameof(IntegerValue),
                SensorType.Double => nameof(DoubleValue),
                SensorType.String => nameof(StringValue),
                SensorType.IntegerBar => nameof(IntegerBarValue),
                SensorType.DoubleBar => nameof(DoubleBarValue),
                SensorType.File => nameof(FileValue),
                _ => null
            };

            return string.Format(SensorValueTypeInvalid, name);
        }

        private static SensorStatus GetFinalStatus(SensorStatus first, SensorStatus second) =>
            first > second ? first : second;
    }
}
