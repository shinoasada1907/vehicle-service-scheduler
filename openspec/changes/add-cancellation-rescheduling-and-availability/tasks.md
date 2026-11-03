# Implementation Plan — add-cancellation-rescheduling-and-availability

Task-level plan, no code written in advance. Scenarios V1–V8, A1–A4, R1–R10, C1–C5, X1–X2 are in `specs/appointment-changes/spec.md`.

## How every task runs

1. Claude presents the approach for the task and the owner approves it.
2. The implementer agent (claude-sonnet-5-5, medium) writes the feature code, then the tests for exactly the listed scenarios (code first, D15).
3. The Opus session reviews the diff, reruns `dotnet build` and `dotnet test`, and shows the real output; the owner approves.
4. One commit per task, green; Claude asks before pushing.

Steps marked 🔒 change configuration, design or documentation and need the owner's permission for that change.

## 1. Decisions and design document 🔒

- [x] 1.1 Record D29 (scope with rescheduling, cut-off, alternatives, error table) in `PROJECT_STATE.md`
- [x] 1.2 Update `docs/system-design.md`: A11, A14, the D8 scope note, the API table, sections 7.3 and 7.4 and a new rescheduling section
- [x] 1.3 Commit `docs: plan cancellation, rescheduling and availability`, then ask before pushing

## 2. Change rules and availability in the domain

- [x] 2.1 Present the approach and get approval
- [x] 2.2 Add the `Cancelled` status and cancellation time to `Appointment`, the change policy (cancelled, cut-off), `SchedulingRules`, and the availability calculator that reuses `ResourceAllocator` (with the alternatives adjustments), plus the EF mapping and migration for `cancelled_at` moved here from 3.2. **Reason:** the new status and property change the EF model; without the migration the integration tests fail with `PendingModelChangesWarning` (25 failures observed), and every commit must be green (D14).
- [x] 2.3 Write unit tests for V1, V2, V4, V5, V6 and A1, A2 at the domain level, and for the change policy behind R6, C4 and X1
- [x] 2.4 Verify, get approval, commit `feat(domain): add change rules and availability`

## 3. Persistence

- [x] 3.1 Present the approach and get approval
- [x] 3.2 🔒 ~~Add the migration (`cancelled_at`, check constraint) and~~ Add the store operations (the migration moved to 2.2, see the reason there): load a dealership-day schedule (optionally without one appointment, plus the vehicle's appointments), cancel by conditional update, move by advisory locks plus conditional update with the exclusion and deadlock handling of booking
- [x] 3.3 Write database tests for C3 (repeat cancel through the store), V3 (a cancelled row does not block a new insert) and the move keeping the row unchanged when the update is rejected (R3 at database level)
- [x] 3.4 Verify, get approval, commit `feat(infrastructure): store cancellations and moves`

## 4. Use cases

- [x] 4.1 Present the approach and get approval
- [x] 4.2 Implement the availability, alternatives, reschedule (bounded retry as booking) and cancel use cases and their ports
- [x] 4.3 Write unit tests with the in-memory store and controllable clock for R1, R3, R4, R5, R6, R7, R8, C1, C3, C4 and A4
- [x] 4.4 Verify, get approval, commit `feat(application): add availability, rescheduling and cancellation`

## 5. HTTP API

- [x] 5.1 Present the approach and get approval
- [x] 5.2 🔒 Add the four endpoints, query and body validation, the new Problem Details types, and `Scheduling:ChangeCutoff` in `appsettings.json`
- [x] 5.3 Write HTTP tests for V1, V3, V5, V7, V8, A1, A3, A4, R1, R2, R3, R7, R9, C1, C2, C3, C4, C5, X1, X2
- [x] 5.4 Verify, get approval, commit `feat(api): add availability, rescheduling and cancellation endpoints`

## 6. Proof under concurrency

- [x] 6.1 Present the approach and get approval
- [x] 6.2 Write the parallel test for R10 (reschedule and booking race for the last free time), and repeat it on freshly started Docker Compose stacks
- [x] 6.3 Verify, get approval, commit `test: prove rescheduling never double books`

## 7. Client stub and README 🔒

- [ ] 7.1 Present the changes and get permission
- [ ] 7.2 Add the new requests to `http/scheduler.http` and the endpoints, errors and cut-off to the README
- [ ] 7.3 Verify every new request against a fresh stack, get approval, commit `docs: document availability, rescheduling and cancellation`

## 8. Close the change

- [ ] 8.1 Opus review of the whole pull request diff; open the pull request after the owner's approval; CI green
- [ ] 8.2 Archive with `openspec archive add-cancellation-rescheduling-and-availability -y` on the branch, then merge with a merge commit after the owner's approval
