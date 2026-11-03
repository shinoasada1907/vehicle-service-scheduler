## Context

`add-appointment-booking` is merged: booking, read back, three exclusion constraints on confirmed appointments (bay, technician, vehicle), retries on lost races and deadlocks, per-resource advisory locks before each insert, Problem Details with project URNs. The owner approved the direction of this change on 2026-11-03 in six questions (scope with rescheduling, a 2-hour configurable cut-off, an alternatives endpoint, time-only rescheduling, the error table, availability edge cases) and the 29 test scenarios V1–V8, A1–A4, R1–R10, C1–C5, X1–X2.

## Goals / Non-Goals

**Goals:** show free times with the exact booking rule; move an appointment without ever losing the old slot first; cancel safely and idempotently; enforce one change cut-off everywhere; stay correct under concurrent requests on several API instances.

**Non-Goals:** moving to another dealership or service; per-dealership cut-offs; caching availability; authorisation of who may cancel (no authentication yet, as A16); notifications.

## Decisions

### 1. Availability is computed by the domain with the booking allocator
A domain `AvailabilityCalculator` takes the dealership, service type, local date, current time, bays, technicians and the confirmed appointments of that local day. It generates candidate starts every 15 minutes from opening time to closing time minus the duration, drops starts that are not after now, and keeps a candidate only when `ResourceAllocator.Allocate` returns `Allocated` for its range. Availability and booking therefore cannot disagree. Service-not-offered is detected once, before the loop, with the same allocator result. *Alternative:* a SQL query generating free ranges; rejected because the rules would be duplicated outside the tested domain.

### 2. Alternatives reuse the calculator with two adjustments
For `GET /appointments/{id}/alternatives` the appointment's own row is removed from the busy list (so a 30-minute shift that overlaps itself is offered) and the vehicle's other confirmed appointments of that day are added as a filter (a candidate that overlaps one of them is dropped). The date defaults to the appointment's local date.

### 3. Change rules live in the domain
`ChangePolicy.Check(appointment, now, cutoff)` returns `AppointmentCancelled` when the status is `Cancelled` and `ChangeWindowClosed` when `appointment.StartAt - now <= cutoff` (exactly at the cut-off is closed). Cancel treats `AppointmentCancelled` as success with the current state; reschedule and alternatives turn it into 422. The cut-off is a `SchedulingRules(TimeSpan ChangeCutoff)` record built in the Api from `Scheduling:ChangeCutoff` (default `02:00:00`) and injected into the use cases, so Application does not depend on configuration packages.

### 4. Cancel is one conditional update
The store runs `UPDATE appointments SET status = 'Cancelled', cancelled_at = @now WHERE id = @id AND status = 'Confirmed' AND start_at > @now + @cutoff`. One row means cancelled. Zero rows means: reload and report not found, already cancelled (200 with the current state) or window closed. No check-then-write race with a concurrent reschedule or cancel: the database row decides.

### 5. Reschedule updates the row in place, with the booking's safeguards
The use case loads the appointment, applies the change policy and the booking policy for the new range (same id, customer and vehicle; ownership is unchanged), loads the day's schedule without the appointment itself and allocates a bay and technician. The store then, in one transaction: takes the advisory locks for the new bay, technician and vehicle (same keys and order as booking), and runs `UPDATE appointments SET start_at, end_at, service_bay_id, technician_id WHERE id = @id AND status = 'Confirmed' AND start_at = @oldStart AND start_at > @now + @cutoff`. The exclusion constraints compare the new range with every other row, so the appointment's own old range never conflicts. Outcomes: updated → 200; zero rows → reload and report the current reason (cancelled, window closed, or moved by someone else, which is retried); `23P01` on bay or technician → reload and retry within the same three attempts as booking; vehicle → 409 `vehicle-already-booked`; `40P01` → retry. A failed move leaves the row untouched because the transaction rolls back. A new start equal to the old one returns 200 without writing.

### 6. Data model
`AppointmentStatus` gains `Cancelled`. Table `appointments` gains `cancelled_at timestamptz NULL` and the check constraint `ck_appointments_cancelled_at` (`(status = 'Cancelled') = (cancelled_at IS NOT NULL)`). One migration. The exclusion constraints already use `WHERE status = 'Confirmed'`, so cancelled rows never block. `AppointmentDetails` gains `CancelledAt`.

### 7. API
`GET /dealerships/{id}/availability?serviceTypeId=&date=` and `GET /appointments/{id}/alternatives?date=` return `{ dealershipId, serviceTypeId, date, timeZone, slots: [{ startAt, endAt }] }`. `date` is validated manually as `yyyy-MM-dd` (400 `validation` per field). `POST /appointments/{id}/reschedule` takes `{ startAt }` with the same explicit-offset rule as booking. `POST /appointments/{id}/cancel` has no body. New slugs `change-window-closed` (detail states the cut-off) and `appointment-cancelled`; the rest reuse booking's slugs.

### 8. Documentation
`docs/system-design.md`: A11 and A14 rewritten, D8 scope note, API table, sections 7.3 (availability), 7.4 (cancellation) and a new reschedule section. README and `http/scheduler.http` gain the new endpoints.

## Risks / Trade-offs

- **[Availability is a snapshot]** A listed time can be taken before it is booked → the booking or reschedule returns 409 and the client refreshes the list.
- **[Day-wide schedule load]** Availability loads one dealership-day of appointments (tens of rows) → cheap at this scale; caching is future work.
- **[Advisory locks are per resource, not per time]** Same trade-off as booking (LOW).
- **[Cut-off measured on the server clock]** Uses the injected `TimeProvider`; tests control it.

## Migration Plan

One additive migration (nullable column, check constraint valid for existing confirmed rows). Rollback drops both.

## Open Questions

None.
