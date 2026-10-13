namespace HSMServer.Core.Cache
{
    // One template policy a matching sensor REJECTED at apply time (#1401).
    // The save itself persisted, but this alert was not (re)created on the
    // sensor — SensorPolicyCollection.TryUpdate refused it with per-policy
    // validation noise (e.g. an AnyType template's Value condition on a bar
    // sensor). Before #1401 the rejection was a log line only: the operator
    // saw a saved template with an alert that "isn't there" and no journal
    // trace. The rejection does NOT fail the save that persisted — it rides
    // the warnings channel of AddAlertTemplateAsync's result instead.
    public sealed record TemplateApplyWarning(string SensorPath, string Reason);
}
