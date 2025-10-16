//using HSMDatabase.AccessManager.DatabaseEntities;
//using HSMServer.Core.Model;
//using HSMServer.Core.Model.Policies;
//using Moq;
//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using System.Threading.Tasks;
//using Xunit;

//namespace HSMServer.Core.Tests.SensorModel
//{

//    public abstract class BaseSensorModelTests<T> where T : BaseValue, new()
//    {
//        protected abstract BaseSensorModel<T> CreateSensorModel();

//        protected T CreateTestValue(DateTime time, bool timeout = false)
//        {
//            return new T
//            {
//                Time = time,
//                IsTimeout = timeout,
//            };
//        }

//        protected readonly Mock<SensorPolicyCollection<T>> _mockPolicies;
//        protected readonly Mock<ValuesStorage<T>> _mockStorage;

//        public BaseSensorModelTests()
//        {
//            _mockPolicies = new Mock<SensorPolicyCollection<T>>();
//            _mockStorage = new Mock<ValuesStorage<T>>();
//        }



//        [Fact]
//        public void TryAddValue_TimeoutValue_AlwaysStored()
//        {
//            // Arrange
//            var sensor = CreateSensorModel();
//            var timeoutValue = CreateTestValue(DateTime.UtcNow, true);

//            _mockPolicies.Setup(p => p.TryValidate(It.IsAny<BaseValue>(), out It.Ref<T>.IsAny, It.IsAny<bool>()))
//                         .Returns(false);

//            // Act
//            var result = sensor.TryAddValue(timeoutValue);

//            // Assert
//            Assert.True(result);
//            _mockStorage.Verify(s => s.AddValueBase(It.IsAny<T>()), Times.Once);
//        }

//        //[Fact]
//        //public void TryAddValue_Singleton_NotNewValue_ReturnsFalse()
//        //{
//        //    // Arrange
//        //    var sensor = CreateSensorModel();
//        //    sensor.IsSingleton = true;

//        //    var value = CreateTestValue(DateTime.UtcNow);

//        //    _mockStorage.Setup(s => s.IsNewSingletonValue(It.IsAny<BaseValue>()))
//        //               .Returns(false);

//        //    // Act
//        //    var result = sensor.TryAddValue(value);

//        //    // Assert
//        //    Assert.False(result);
//        //}

//        [Fact]
//        public void TryAddValue_ValidValue_StoresAndInvokesEvent()
//        {
//            // Arrange
//            var sensor = CreateSensorModel();
//            var value = CreateTestValue(DateTime.UtcNow);
//            var validatedValue = value;

//            bool eventRaised = false;
//            sensor.ReceivedNewValue += v => eventRaised = true;

//            _mockPolicies.Setup(p => p.TryValidate(value, out validatedValue, It.IsAny<bool>()))
//                         .Returns(true);

//            // Act
//            var result = sensor.TryAddValue(value);

//            // Assert
//            Assert.True(result);
//            Assert.True(eventRaised);
//            _mockStorage.Verify(s => s.AddValue(validatedValue), Times.Once);
//        }

//        [Fact]
//        public void TryAddValue_InvalidValue_DoesNotStore()
//        {
//            // Arrange
//            var sensor = CreateSensorModel();
//            var value = CreateTestValue(DateTime.UtcNow);
//            var validatedValue = value;

//            _mockPolicies.Setup(p => p.TryValidate(value, out validatedValue, It.IsAny<bool>()))
//                         .Returns(false);

//            // Act
//            var result = sensor.TryAddValue(value);

//            // Assert
//            Assert.False(result);
//            _mockStorage.Verify(s => s.AddValue(It.IsAny<T>()), Times.Never);
//        }

//        [Fact]
//        public void TryUpdateLastValue_ValidUpdate_UpdatesAndInvokesEvent()
//        {
//            // Arrange
//            var sensor = CreateSensorModel();
//            var value = CreateTestValue(DateTime.UtcNow);

//            bool eventRaised = false;
//            sensor.ReceivedNewValue += v => eventRaised = true;

//            _mockStorage.Setup(s => s.TryChangeLastValue(value)).Returns(true);
//            _mockPolicies.Setup(p => p.TryRevalidate(value)).Returns(true);

//            // Act
//            var result = sensor.TryUpdateLastValue(value);

//            // Assert
//            Assert.True(result);
//            Assert.True(eventRaised);
//        }

//        [Fact]
//        public void TryUpdateLastValue_StorageRejects_ReturnsFalse()
//        {
//            // Arrange
//            var sensor = CreateSensorModel();
//            var value = CreateTestValue(DateTime.UtcNow);

//            _mockStorage.Setup(s => s.TryChangeLastValue(value)).Returns(false);

//            // Act
//            var result = sensor.TryUpdateLastValue(value);

//            // Assert
//            Assert.False(result);
//        }

//        [Fact]
//        public void Revalidate_WithLastValue_Revalidates()
//        {
//            // Arrange
//            var sensor = CreateSensorModel();
//            var lastValue = CreateTestValue(DateTime.UtcNow);

//            _mockStorage.SetupGet(s => s.LastValue).Returns(lastValue);

//            // Act
//            sensor.Revalidate();

//            // Assert
//            _mockPolicies.Verify(p => p.TryRevalidate(lastValue), Times.Once);
//        }

//        [Fact]
//        public void CheckTimeout_CallsSensorTimeout()
//        {
//            // Arrange
//            var sensor = CreateSensorModel();
//            var lastValue = CreateTestValue(DateTime.UtcNow);

//            _mockStorage.SetupGet(s => s.LastValue).Returns(lastValue);

//            // Act
//            sensor.CheckTimeout();

//            // Assert
//            _mockPolicies.Verify(p => p.SensorTimeout(lastValue), Times.Once);
//        }

//        [Fact]
//        public void AddDbValue_TimeoutValue_AlwaysStored()
//        {
//            // Arrange
//            var sensor = CreateSensorModel();
//            var timeoutValue = CreateTestValue(DateTime.UtcNow, true);


//            byte[] bytes = timeoutValue.ToBytes();

//            _mockPolicies.Setup(p => p.TryValidate(It.IsAny<BaseValue>(), out It.Ref<T>.IsAny))
//                         .Returns(false);

//            // Act
//            sensor.AddDbValue(bytes);

//            // Assert
//            _mockStorage.Verify(s => s.AddValue(It.IsAny<T>()), Times.Once);
//        }

//        [Fact]
//        public void AddDbValue_ValidValue_Stored()
//        {
//            // Arrange
//            var sensor = CreateSensorModel();
//            var value = CreateTestValue(DateTime.UtcNow);
//            byte[] bytes = value.ToBytes();

//            _mockPolicies.Setup(p => p.TryValidate(It.IsAny<BaseValue>(), out It.Ref<T>.IsAny))
//                         .Returns(true);

//            // Act
//            sensor.AddDbValue(bytes);

//            // Assert
//            _mockStorage.Verify(s => s.AddValue(It.IsAny<T>()), Times.Once);
//        }
//    }

//    // Example concrete implementation for testing
//    public class ConcreteSensorModelTests : BaseSensorModelTests<ConcreteValue>
//    {
//        protected override BaseSensorModel<ConcreteValue> CreateSensorModel()
//        {
//            var sensor = new Mock<BaseSensorModel<ConcreteValue>>();
//            sensor.SetupGet(s => s.Policies).Returns(_mockPolicies.Object);
//            sensor.SetupGet(s => s.Storage).Returns(_mockStorage.Object);
//            return sensor.Object;
//        }

//        protected override ConcreteValue CreateTestValue(DateTime time)
//        {
//            return new ConcreteValue { Time = time };
//        }
//    }

//    // Example concrete value class for testing
//    public class ConcreteValue : BaseValue
//    {
//        // Implement any abstract members from BaseValue
//        public override BaseValue TrySetValue(string str)
//        {
//            throw new NotImplementedException();
//        }

//        public override BaseValue TrySetValue(BaseValue baseValue)
//        {
//            throw new NotImplementedException();
//        }
//    }
//}
