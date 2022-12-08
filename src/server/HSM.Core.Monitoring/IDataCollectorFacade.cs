namespace HSM.Core.Monitoring
{
    public interface IDataCollectorFacade
    {
        #region Load reporting
        void ReportRequestSize(double size);
        void ReportSensorsCount(int count);
        void ReportResponseSize(double size);
        void IncreaseRequestsCount(int count = 1);

        #endregion

        #region Database size reporting

        void ReportDatabaseSize(long bytesSize);
        void ReportSensorsHistoryDataSize(long bytesSize);
        void ReportEnvironmentDataSize(long bytesSize);

        #endregion
    }
}