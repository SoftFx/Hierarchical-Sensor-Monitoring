namespace HSMDataCollector.Core
{
    /// <summary>
    /// Optional collector capability for callers that need to know whether new sensors may be
    /// registered without extending the compatibility-sensitive <see cref="IDataCollector"/>.
    /// </summary>
    public interface ICollectorRegistrationState
    {
        /// <summary>
        /// True while new sensors may be registered: during the configuration phase (collector
        /// Stopped, sensors are queued and started on the next <see cref="IDataCollector.Start()"/>)
        /// and the operational phase (Starting/Running, sensors start immediately). False while the
        /// collector is Stopping or after it has been disposed.
        /// </summary>
        bool IsAcceptingRegistrations { get; }
    }
}
