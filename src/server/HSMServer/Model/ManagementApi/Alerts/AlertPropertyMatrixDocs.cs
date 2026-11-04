namespace HSMServer.Model.ManagementApi.Alerts
{
    // The agent-facing documentation of the condition-property availability
    // matrix (#1556) — the single source both condition DTOs reference from
    // their [Description]s (the name-valued policy surface and the byte-valued
    // alert-template surface). The lines mirror AlertPolicyConditionRules —
    // the table behind the 422 validation of sensor-policy writes. The
    // template write does not consult the rule table itself: it reconstructs
    // every policy with the domain class of the template's sensor type (a
    // Boolean stand-in for AnyType), so a non-offered pair — and the one
    // table-offered pair the executor cannot build, Enum's EmaValue — fails
    // the whole write with a parse error; the header sentence says so (#1609:
    // the old 'checks nothing, the rest are stored silently' text described
    // the pre-reconstruction era and disagreed with the write). One line per
    // type group, " — " between the types and their properties,
    // parentheticals are prose. The drift guard (AlertSchemaDocsTests) parses
    // these lines back and compares the (sensor type, property) pairs with
    // the rule table — edit this text and the rules together.
    internal static class AlertPropertyMatrixDocs
    {
        internal const string Matrix =
            @"Condition-property availability by sensor type — exhaustive, negatives included: on the policy surface a property the type does not offer answers 422. The template write enforces this table, and more: it reconstructs every policy with the template's sensor type (an AnyType template against a Boolean stand-in, so only the common set below passes its write), and a property the type does not offer, or one whose typed executor the domain cannot build (Enum's EmaValue), fails the whole write with a parse error naming the reason (Count on a Double template answers 'The template could not be parsed: Not supported property Count for DoublePolicy'). Nothing non-offered is stored by a write; a policy that does reach sensors can still be rejected per sensor at apply time, and those rejections ride the response's `warnings` instead.
every type — Status, Comment, NewSensorData
Boolean — nothing beyond the common set (no Value)
Integer, Double, Rate, Enum — Value, EmaValue (Enum EmaValue: accepted by validation, currently fails at apply time)
TimeSpan, Version — Value
String — Value, Length
File — OriginalSize (no Value)
IntegerBar, DoubleBar — Min, Max, Mean, Count, FirstValue, LastValue, EmaMin, EmaMax, EmaMean, EmaCount (no Value; none of the bar statistics is offered by a single-value type)";
    }
}
