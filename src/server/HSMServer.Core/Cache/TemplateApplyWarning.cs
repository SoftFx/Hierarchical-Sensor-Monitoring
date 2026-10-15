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
    // Since #1565 the same channel also carries the zero-match diagnostic:
    // SensorPath holds the PATH PATTERN itself (there is no sensor to name)
    // when it matched no sensors in the template's folder — a dead pattern
    // the save still persists (sensors may come later), reported so the
    // caller no longer needs a UI round-trip to discover it.
    public sealed record TemplateApplyWarning(string SensorPath, string Reason);
}
