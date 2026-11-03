## Why

A client that times out while booking does not know whether the appointment was created. Retrying can create a second appointment for another time slot or fail with `vehicle-already-booked`, which hides that the first attempt succeeded. SDD section 7.6 and A16 define an optional `Idempotency-Key` header for `POST /appointments`; this is the "could" change of D12, carried out under the owner's delegation of 2026-11-03 (D30, D31).

## What Changes

- `POST /appointments` accepts an optional `Idempotency-Key` header (1–100 visible ASCII characters; clients should use UUIDs).
- The first booking with a key stores the key on the appointment. A retry with the same key and the same request data returns the original appointment with 201 and the same `Location`, without booking again, whatever has happened to the slots since.
- A retry with the same key and different data returns 422 `idempotency-key-reused`.
- Concurrent requests with the same key create at most one appointment: a unique index on the key is the referee.
- Data model: nullable `idempotency_key` column with a unique index (keys are global, A16).

## Capabilities

### New Capabilities
- `idempotent-booking`: the `Idempotency-Key` behaviour of `POST /appointments`.

### Modified Capabilities
None. Bookings without the header behave exactly as before.

## Impact

Domain (`Appointment.IdempotencyKey`), Application (booking command and handler, port method to find by key, store result for a key collision), Infrastructure (migration, store), Api (header validation, new problem type), tests, README and `http/scheduler.http`. No new packages.
