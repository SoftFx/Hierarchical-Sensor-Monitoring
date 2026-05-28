# Operator UX Review Roles

## HSM Site UX Reviewer

Focus:

- server web UI under `Views`, `wwwroot`, TypeScript, dashboards, sensor tree, history, alerts, schedules, templates, registration, and configuration screens;
- operator clarity for monitoring status, alert state, data freshness, and navigation through hierarchical sensors;
- loading, empty, error, disabled, hidden, and large-dataset states;
- safe handling of destructive or broad-scope actions.

Must inspect:

- changed views, scripts, CSS, and nearby UI patterns;
- screenshots, Playwright traces, or a running local site when the change is user-reviewable;
- related wiki/user docs when UI behavior is documented.

Output:

- UX findings tied to an exact route/screen/state when possible;
- unclear labels, hidden active state, stale data, confusing hierarchy, or unsafe actions;
- accessibility and responsive layout risks;
- missing Playwright or integration coverage for critical UI flows.

## Dashboard / Monitoring Workflow Reviewer

Focus:

- whether dashboards and sensor views help an operator quickly answer: what is unhealthy, since when, why, and what to inspect next;
- chart/table readability, time-range controls, refresh behavior, and status colors;
- alert/sensor terminology consistency between UI, API, and docs.

Must read:

- changed dashboard/datasource/chart code;
- alert and sensor docs for changed concepts;
- tests or screenshots for populated and empty monitoring states.

Output:

- monitoring workflow blockers;
- misleading chart/table/status behavior;
- missing states or docs for operator troubleshooting.
