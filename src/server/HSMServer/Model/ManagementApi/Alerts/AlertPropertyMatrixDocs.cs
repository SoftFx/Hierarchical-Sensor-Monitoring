namespace HSMServer.Model.ManagementApi.Alerts
{
    // The agent-facing documentation of the condition-property availability
    // matrix (#1556) — the single source both condition DTOs reference from
    // their [Description]s (the name-valued policy surface and the byte-valued
    // alert-template surface). The lines mirror AlertPolicyConditionRules —
    // the table behind BOTH the 422 validation of sensor-policy writes and
    // the template-apply warnings — one line per type group, " — " between
    // the types and their properties, parentheticals are prose. The drift
    // guard (AlertSchemaDocsTests) parses these lines back and compares
    // the (sensor type, property) pairs with the rule table — edit this text
    // and the rules together.
    internal static class AlertPropertyMatrixDocs
    {
        internal const string Matrix =
            @"Condition-property availability by sensor type — exhaustive, negatives included: a property the type does not offer answers 422 on the policy surface and a template-apply warning on the template surface.
every type — Status, Comment, NewSensorData
Boolean — nothing beyond the common set (no Value)
Integer, Double, Rate, Enum — Value, EmaValue (Enum EmaValue: accepted by validation, currently fails at apply time)
TimeSpan, Version — Value
String — Value, Length
File — OriginalSize (no Value)
IntegerBar, DoubleBar — Min, Max, Mean, Count, FirstValue, LastValue, EmaMin, EmaMax, EmaMean, EmaCount (no Value; none of the bar statistics is offered by a single-value type)";
    }
}
