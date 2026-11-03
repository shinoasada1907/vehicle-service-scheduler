# Implementation Plan — add-idempotent-booking

Scenarios I1–I6 are in `specs/idempotent-booking/spec.md`. Delegated by the owner (D30, D31): implementer agent (claude-sonnet-5-5, medium) → Opus review with real build and test output → one green commit per task.

## 1. Plan

- [x] 1.1 Record D31; commit the OpenSpec change `docs: plan idempotent booking`

## 2. Idempotent booking

- [ ] 2.1 Add the key to the appointment, the migration (column and unique index), the store lookup and key-collision result, the handler logic and telemetry outcomes, and the header validation and problem type in the Api
- [ ] 2.2 Write tests: unit (I3 unchanged behaviour is covered by existing tests; I4 comparison), HTTP I1, I2, I4, I6, parallel I5
- [ ] 2.3 Verify and commit `feat: make booking idempotent with Idempotency-Key`

## 3. Client stub and README

- [ ] 3.1 Add the replay and reuse requests to `http/scheduler.http`, document the header in the README and SDD §7.6; verify on a fresh stack; commit `docs: document idempotent booking`

## 4. Close the change

- [ ] 4.1 Opus review of the whole diff; pull request; CI green
- [ ] 4.2 Archive on the branch, then merge with a merge commit (left unticked on purpose: the merge commit on `main` is the evidence)
