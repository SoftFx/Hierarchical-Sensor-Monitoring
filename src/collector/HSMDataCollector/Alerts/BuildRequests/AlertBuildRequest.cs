namespace HSMDataCollector.Alerts
{
    public sealed class SpecialAlertBuildRequest : AlertBuildRequest { }


    public sealed class InstantAlertBuildRequest : AlertBuildRequest { }


    public sealed class BarAlertBuildRequest : AlertBuildRequest { }


    public abstract class AlertBuildRequest
    {
        protected internal AlertBuildRequest() { }
    }
}