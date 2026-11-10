namespace HSMServer.Core.Model.Policies
{
    // #1401/#1563: one per-policy failure collected by the template-apply
    // path's collector (the former List<string> of #1401 carried rejections
    // only). The kind decides how ApplyTemplateToSensor reports the entry:
    // a Rejected policy was refused and never created (journal "not
    // applied: ...", Error-level "partially applied" trace), while an
    // EvaluationError policy APPLIED and persisted and only its post-update
    // revalidation threw (#1563) — the update stands, the journal must not
    // claim "not applied", and the apply logs a warn, not an error.
    internal enum PolicyUpdateFailureKind
    {
        Rejected,
        EvaluationError,
    }

    internal sealed record PolicyUpdateFailure(PolicyUpdateFailureKind Kind, string Reason);
}
