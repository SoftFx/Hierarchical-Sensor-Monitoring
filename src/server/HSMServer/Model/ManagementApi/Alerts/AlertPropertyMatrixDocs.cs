namespace HSMServer.Model.ManagementApi.Alerts
{
    // The agent-facing documentation of the condition-property availability
    // matrix (#1556) — the single source both condition DTOs reference from
    // their [Description]s (the name-valued policy surface and the byte-valued
    // alert-template surface). The lines mirror AlertPolicyConditionRules —
    // the table behind the 422 validation of sensor-policy writes; the
    // template surface does not consult it at all (its apply warnings come
    // from the executor builder, which misses some non-offered pairs — the
    // header says which fail silently instead) — one line per type group, " — " between
    // the types and their properties, parentheticals are prose. The drift
    // guard (AlertSchemaDocsTests) parses these lines back and compares
    // the (sensor type, property) pairs with the rule table — edit this text
    // and the rules together.
    internal static class AlertPropertyMatrixDocs
    {
        internal const string Matrix =
            @"Condition-property availability by sensor type — exhaustive, negatives included: on the policy surface a property the type does not offer answers 422. The template surface checks nothing against this table: only some non-offered pairs are rejected with a template-apply warning, the rest are stored silently and fail at every evaluation (the bar statistics on Integer, Double and Rate sensors, and Length, Count or OriginalSize on a type that does not offer them).
every type — Status, Comment, NewSensorData
Boolean — nothing beyond the common set (no Value)
Integer, Double, Rate, Enum — Value, EmaValue (Enum EmaValue: accepted by validation, currently fails at apply time)
TimeSpan, Version — Value
String — Value, Length
File — OriginalSize (no Value)
IntegerBar, DoubleBar — Min, Max, Mean, Count, FirstValue, LastValue, EmaMin, EmaMax, EmaMean, EmaCount (no Value; none of the bar statistics is offered by a single-value type)";
    }
}
