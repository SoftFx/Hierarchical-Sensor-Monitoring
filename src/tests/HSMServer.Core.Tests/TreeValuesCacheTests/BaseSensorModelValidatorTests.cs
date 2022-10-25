using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache;
using HSMServer.Core.Model;
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
        private const string SensorValueOutdated = "Sensor value is older than ExpectedUpdateInterval!";
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

        private readonly ITreeValuesCache _valuesCache;


        public BaseSensorModelValidatorTests(ValidationFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture, addTestProduct: false)
        {
            _valuesCache = new TreeValuesCache(_databaseCoreManager.DatabaseCore, _userManager, _updatesQueue);
        }


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

            Assert.False(sensor.TryAddValue(null, out _));
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

            Assert.True(sensor.TryAddValue(stringBase, out _));
            Assert.True(sensor.ValidationResult.IsWarning);
            Assert.Equal(SensorStatus.Warning, sensor.ValidationResult.Result);
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

                Assert.False(sensor.TryAddValue(SensorValuesFactory.BuildSensorValue(invalidType), out _));
                Assert.True(sensor.ValidationResult.IsError);
                Assert.Equal(SensorStatus.Error, sensor.ValidationResult.Result);
                Assert.Equal(errorMessage, sensor.ValidationResult.Message);
            }
        }

        [Theory]
        [InlineData(SensorStatus.Unknown)]
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

                Assert.True(sensor.TryAddValue(baseValue, out _));
                Assert.Equal(baseValue.Status, sensor.ValidationResult.Result);
                Assert.Equal(expectedMessage, sensor.ValidationResult.Message);

                if (status == SensorStatus.Error)
                    Assert.True(sensor.ValidationResult.IsError);
                else if (status == SensorStatus.Warning)
                    Assert.True(sensor.ValidationResult.IsWarning);
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

                Assert.True(sensor.TryAddValue(baseValue, out _));
                Assert.True(sensor.CheckExpectedUpdateInterval());
                Assert.True(sensor.ValidationResult.IsWarning);
                Assert.Equal(SensorStatus.Warning, sensor.ValidationResult.Result);
                Assert.Equal(SensorValueOutdated, sensor.ValidationResult.Message);
            }
        }

        [Theory]
        [InlineData(SensorStatus.Unknown)]
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

            Assert.True(sensor.TryAddValue(stringBase, out _));
            Assert.True(sensor.ValidationResult.IsWarning);
            Assert.Equal(GetFinalStatus(status, SensorStatus.Warning), sensor.ValidationResult.Result);

            if (status == SensorStatus.Error)
                Assert.True(sensor.ValidationResult.IsError);
        }

        [Theory]
        [InlineData(SensorStatus.Unknown)]
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

                Assert.True(sensor.TryAddValue(baseValue, out _));
                Assert.True(sensor.CheckExpectedUpdateInterval());
                Assert.True(sensor.ValidationResult.IsWarning);
                Assert.Equal(GetFinalStatus(status, SensorStatus.Warning), sensor.ValidationResult.Result);

                if (status == SensorStatus.Error)
                    Assert.True(sensor.ValidationResult.IsError);
            }
        }

        [Fact]
        [Trait("Category", "EmptyPathOrKey")]
        public void EmptyPathOrKeyValidationTest()
        {
            var info = new StoreInfo();

            Assert.False(_valuesCache.TryCheckKeyWritePermissions(info, out var message));
            Assert.Equal(ErrorPathKey, message);
        }

        [Fact]
        [Trait("Category", "TooLongPath")]
        public void TooLongPathValidationTest()
        {
            var info = new StoreInfo
            {
                Key = Guid.NewGuid().ToString(),
                Path = InvalidTooLongPath
            };

            Assert.False(_valuesCache.TryCheckKeyWritePermissions(info, out var message));
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
            var info = new StoreInfo
            {
                Key = Guid.NewGuid().ToString(),
                Path = path
            };

            Assert.False(_valuesCache.TryCheckKeyWritePermissions(info, out var message));
            Assert.Equal(ErrorInvalidPath, message);
        }

        [Fact]
        [Trait("Category", "InvalidKey")]
        public void InvalidKeyValidationTest()
        {
            var info = new StoreInfo
            {
                Key = Guid.NewGuid().ToString(),
                Path = ValidPath
            };

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
            var accessKey = new AccessKeyModel(EntitiesFactory.BuildAccessKeyEntity(productId: product.Id, permissions: permission));

            _valuesCache.AddAccessKey(accessKey);

            var info = new StoreInfo
            {
                Key = accessKey.Id.ToString(),
                Path = ValidPath
            };

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
