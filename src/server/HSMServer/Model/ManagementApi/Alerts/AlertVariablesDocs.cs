namespace HSMServer.Model.ManagementApi.Alerts
{
    // The agent-facing documentation of the alert-template variable set (#1556)
    // — the single source both template-bearing DTOs reference from their
    // [Description]s (the notification field of the policy surface and the
    // template field of the alert-template surface), so the two input schemas
    // cannot drift. One line per variable, "<variable> — <description>" with
    // the description VERBATIM from AlertState's [AlertVariable]s; the drift
    // guard (AlertSchemaDocsTests) re-derives the expected set from
    // AlertState.VariablesHelp at runtime — edit this text and AlertState
    // together.
    internal static class AlertVariablesDocs
    {
        internal const string Variables =
            @"Message template — the variables below are substituted as substrings (a longer token containing a variable has that variable replaced inside it); curly braces are not allowed: the message is rendered with string.Format, where braces either throw at evaluation time or silently substitute another variable's value; the template is split on spaces, so whitespace touching a space is dropped and whitespace with no space in it is kept:
$product — Parent product name
$path — Sensor path
$sensor — Sensor name
$unit — Sensor unit
$status — Sensor status
$time — Sensor value sending time
$comment — Sensor value comment
$prevStatus — Status of the previous sensor data
$prevComment — Comment of the previous sensor data
$prevValue — Value of the previous sensor data
$value — Sensor Value
$emaValue — Sensor EMA (Value) value
$min — Bar sensor Min value
$max — Bar sensor Max value
$mean — Bar sensor Mean value
$firstValue — Bar sensor FirstValue value
$lastValue — Bar sensor LastValue value
$count — Bar sensor Count value
$emaMin — Bar sensor EMA (Min) value
$emaMax — Bar sensor EMA (Max) value
$emaMean — Bar sensor EMA (Mean) value
$emaCount — Bar sensor EMA (Count) value
$property — Alert property
$operation — Alert operation
$target — Alert constant to compare";
    }
}
