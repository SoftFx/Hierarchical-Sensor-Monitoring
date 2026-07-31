---
name: implement
description: "Implement a piece of work based on a spec or set of tickets."
disable-model-invocation: true
---

Implement the work described by the user in the spec or tickets.

Use /matt:tdd where possible, at pre-agreed seams.

Run typechecking regularly, single test files regularly, and the full test suite once at the end.

Once done, use /matt:code-review to review the work.

Commit your work to the current branch — check first that it is a feature branch, never `master`/`main`, and confirm with the user before the first commit.
