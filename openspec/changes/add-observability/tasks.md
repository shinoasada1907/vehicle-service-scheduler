# Implementation Plan — add-observability

Task-level plan, no code written in advance. Scenarios O1–O8 are in `specs/observability/spec.md`. The owner asked on 2026-11-03 for this change to be carried out end to end without further approval steps (D30); each task still goes implementer agent (claude-sonnet-5-5, medium) → Opus review with real build and test output → one green commit.

## 1. Plan 🔒

- [x] 1.1 Record D30 in `PROJECT_STATE.md`; commit the OpenSpec change `docs: plan observability`

## 2. Business instrumentation

- [x] 2.1 Add `SchedulerTelemetry` (activity source and meter `Scheduler`) and instrument the booking, reschedule and cancel use cases
- [x] 2.2 Write unit tests for O4, O5, O6, O7 with `MeterListener` and `ActivityListener`
- [x] 2.3 Verify and commit `feat(application): record business metrics and traces`

## 3. Telemetry, logs and health in the API 🔒

- [x] 3.1 Add the approved packages; register OpenTelemetry (export only when configured), key-event logs, `/health/live` and `/health/ready`
- [x] 3.2 Write HTTP tests for O1, O2, O3
- [x] 3.3 Verify and commit `feat(api): export telemetry and expose health checks`

## 4. Local run and documentation 🔒

- [x] 4.1 Add the Aspire Dashboard to Docker Compose, `API_PORT` and `DASHBOARD_PORT`, JSON console logs; update `.env.example`
- [x] 4.2 Update the README (observability, deploying to production, ports) and `docs/system-design.md` (health paths, metric names, `API_PORT`)
- [x] 4.3 Verify O8 by hand on a fresh stack (dashboard resource, trace, metric, logs; health endpoints) and commit `build: run the Aspire Dashboard and document operations`

## 5. Close the change

- [ ] 5.1 Opus review of the whole diff; open the pull request; CI green
- [ ] 5.2 Archive on the branch, then merge with a merge commit (left unticked on purpose: the merge commit on `main` is the evidence)
