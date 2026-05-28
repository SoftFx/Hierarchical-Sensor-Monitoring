# Development Lifecycle

> Owner: shared | Last reviewed: 2026-05-28 | Canonical: yes

HSM uses a lightweight lifecycle: understand the change, document the intended
behavior, implement narrowly, review with focused roles, fix blockers, and hand
off without merging.

## Stages

1. **Context / Requirements**
   - Read `AGENTS.md`, `aicontext/README.md`, `aicontext/glossary.md`, and relevant feature docs.
   - Create or update feature docs when the behavior is not yet documented.

2. **Implementation**
   - Keep edits scoped.
   - Add or update tests at the right layer.
   - Update docs alongside code when behavior changes.

3. **Review / Fix Loop**
   - Use [technical_review_orchestration.md](technical_review_orchestration.md).
   - Launch role reviewers selected by changed surfaces.
   - Fix confirmed blockers.
   - Rerun only focused roles after fixes.

4. **Handoff**
   - Summarize changes, tests, residual risks, and check status.
   - Do not merge; human maintainer owns final merge.

## Related Docs

- Review orchestration: [technical_review_orchestration.md](technical_review_orchestration.md)
- Branch / PR workflow: [../task-branch-pr-workflow.md](../task-branch-pr-workflow.md)
- Review roles: [review_roles/index.md](review_roles/index.md)
- Review/fix loop skill: [skills/dev-review-fix-loop/SKILL.md](skills/dev-review-fix-loop/SKILL.md)
