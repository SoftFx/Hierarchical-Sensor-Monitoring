using HSMCommon;
using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;
using HSMServer.Authentication;
using HSMServer.Configuration;
using HSMServer.DataLayer;
using HSMServer.MonitoringServerCore;
using HSMServer.Products;
using Moq;
using Xunit;

namespace HSMServer.Tests.ServerCoreTests
{
    public class MonitoringCoreTests
    {
        //[Fact]
        //public void BoolSensorValuesEnqueued()
        //{
        //    //Arrange
        //    var databaseMock = new Mock<IDatabaseClass>();
        //    var userManagerMock = new Mock<UserManager>();
        //    var barStorageMock = new Mock<IBarSensorsStorage>();
        //    IProductManager productManager = new ProductManager(databaseMock.Object);
        //    var configurationProviderMock = new Mock<IConfigurationProvider>();
        //    IMonitoringCore monitoringCore = new MonitoringCore(databaseMock.Object, userManagerMock.Object,
        //        barStorageMock.Object, productManager, configurationProviderMock.Object);

        //    //Act

        //}



        //#region Generate data methods

        //private BoolSensorValue CreateBoolSensorValueTrueStatusOk()
        //{
        //    BoolSensorValue res = new BoolSensorValue();
        //    res.BoolValue = true;
        //    res.Path = "Test/Test/Bool";
        //    res.Comment = "test sensor with true";
        //    res.Status = SensorStatus.Ok;
        //    res.Key = CommonConstants.De;
        //}

        //#endregion
    }
}
