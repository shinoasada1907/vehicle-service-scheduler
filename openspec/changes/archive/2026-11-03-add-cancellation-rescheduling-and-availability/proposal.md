## Why

Customers can book an appointment but cannot see which times are free, move an appointment, or cancel it. Today a change of plan means cancelling and booking again, which can lose the slot to someone else in between. This change adds the "should" scope of D8 and D12, extended by the owner on 2026-11-03 with atomic rescheduling and a change cut-off.

## What Changes

- `GET /dealerships/{id}/availability?serviceTypeId=&date=` lists the free start times of one local day, using the same rule as booking.
- `GET /appointments/{id}/alternatives?date=` lists free times for an existing appointment, treating its own slot as free and the vehicle's other appointments as busy, so the customer can look before cancelling.
- `POST /appointments/{id}/reschedule` moves an appointment to another start time in one atomic step: same id, dealership, service, customer and vehicle; bay and technician are chosen again; on failure the old appointment is unchanged.
- `POST /appointments/{id}/cancel` cancels an appointment and frees its slot at once; cancelling again returns the current state.
- Cancelling or rescheduling is allowed only while the appointment starts more than a configurable cut-off from now (`Scheduling:ChangeCutoff`, default 2 hours).
- New Problem Details types: `change-window-closed`, `appointment-cancelled`.
- Data model: appointment status `Cancelled` and a `cancelled_at` time.

**Changes to earlier decisions:** A11 (rescheduling was cancel + book) and A14 (cancel only before start) are replaced; rescheduling moves from "won't" to this change (D8). Recorded as D29 in `PROJECT_STATE.md`.

## Capabilities

### New Capabilities
- `appointment-changes`: availability lookup, alternatives for an existing appointment, rescheduling and cancellation, with the change cut-off.

### Modified Capabilities
None. `appointment-booking` keeps its requirements; its exclusion constraints already ignore cancelled appointments.

## Impact

- Domain: appointment status and change rules, an availability calculator that reuses `ResourceAllocator`.
- Application: four new use cases and their ports.
- Infrastructure: one migration (`cancelled_at`, status check), store operations for cancel and move (advisory locks as in booking), schedule queries for a whole day.
- Api: four endpoints, `Scheduling` configuration, new Problem Details types.
- Tests, `http/scheduler.http`, README and `docs/system-design.md` (A11, A14, API table, sections 7.3 availability, 7.4 cancellation and the new 7.5 rescheduling; idempotent booking moves to 7.6).
- No new packages.
