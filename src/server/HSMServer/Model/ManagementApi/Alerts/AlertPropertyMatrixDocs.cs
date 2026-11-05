namespace HSMServer.Model.ManagementApi.Alerts
{
    // The agent-facing documentation of the condition-property availability
    // matrix (#1556) — the single source both condition DTOs reference from
    // their [Description]s (the name-valued policy surface and the byte-valued
    // alert-template surface). The lines mirror AlertPolicyConditionRules —
    // the table behind the 422 validation of sensor-policy writes. Since
    // #1611 the template write consults the same table for every condition
    // BEFORE the domain reconstruction, answering the policy surface's
    // field-keyed message catalogue; the header sentence says so, and the
    // quoted example is pinned verbatim by
    // AlertTemplateConditionValidatorTests (the quote cannot drift from the
    // write). One line per type group, " — " between the types and their
    // properties, parentheticals are prose. The drift guard
    // (AlertSchemaDocsTests) parses these lines back and compares the
    // (sensor type, property) pairs with the rule table — edit this text
    // and the rules together.
    internal static class AlertPropertyMatrixDocs
    {
        internal const string Matrix =
            @"Condition-property availability by sensor type — exhaustive, negatives included: on the policy surface a property the type does not offer answers 422. The template write validates every condition against this table before reconstructing the template (#1611): a property the type does not offer, an operation the property does not accept, or an unparsable target value answers a field-keyed 400 carrying this surface's message catalogue (a Count condition on a Double template answers policies[0].conditions[0].property: Property 'Count' is not supported by Double templates. Supported: Value, EmaValue, Status, Comment, NewSensorData.); an AnyType template accepts only the common set below, and its errors name AnyType, never the Boolean stand-in the reconstruction uses. Two table-offered pairs still fail the write the same way because the domain executors cannot build them: Enum's EmaValue, and the text operations Contains/StartsWith/EndsWith on Version's Value (the six comparisons are what Version actually runs). Nothing non-offered is stored by a write; a policy that does reach sensors can still be rejected per sensor at apply time, and those rejections ride the response's `warnings` instead.
every type — Status, Comment, NewSensorData
Boolean — nothing beyond the common set (no Value)
Integer, Double, Rate, Enum — Value, EmaValue (Enum EmaValue: table-offered, but the template write rejects it — no typed executor)
TimeSpan, Version — Value (Version's Value: a template write runs the six comparisons; a sensor-policy write offers the text operations instead, #1608)
String — Value, Length
File — OriginalSize (no Value)
IntegerBar, DoubleBar — Min, Max, Mean, Count, FirstValue, LastValue, EmaMin, EmaMax, EmaMean, EmaCount (no Value; none of the bar statistics is offered by a single-value type)";
    }
}
