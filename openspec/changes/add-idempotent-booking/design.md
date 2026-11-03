## Context

Booking, availability/rescheduling/cancellation and observability are merged. SDD §7.6 and A16 describe idempotent booking; the owner delegated this change to Claude on 2026-11-03 (D30) and asked for everything to be finished the same day; the choices below are recorded as D31.

## Goals / Non-Goals

**Goals:** a safe retry of `POST /appointments`; at most one appointment per key, also under concurrency; no behaviour change without the header.

**Non-Goals:** idempotency for reschedule and cancel (cancel is already idempotent; reschedule to the same time is a no-op), key expiry, per-client key scope (needs authentication, A16).

## Decisions

### 1. The key is stored on the appointment
`appointments.idempotency_key` (nullable `varchar(100)`) with a unique index. Keeping it on the row avoids a second table and keeps the original request data (customer, vehicle, dealership, service type, start) available for the comparison. Keys never expire in this version.

### 2. Look up first, then book, with the unique index as referee
The handler first asks the store for an appointment with the key. Found → compare the request with the stored appointment: same customer, vehicle, dealership, service type and start (UTC) → return the original as `Booked` (201, same `Location`); otherwise → `IdempotencyKeyReused` (422). Not found → book as today, with the key on the new appointment. If the insert fails because another request with the same key won (`23505` on `ux_appointments_idempotency_key`), the store reports `KeyTaken`, the handler loads the winner and applies the same comparison. The rules run before availability, so a retry returns the original even if the slot is now taken by itself.

### 3. Rules before the key? No — the key first
A retry of a successful booking must succeed even when the start is now in the past or the vehicle is now booked (by the original). So the key lookup happens right after reference loading and before the booking rules.

### 4. API
Header `Idempotency-Key`, optional. Invalid (empty, longer than 100, non-visible ASCII) → 400 `validation` with an error for `Idempotency-Key`. New problem type `idempotency-key-reused` (422). The response for a replay is identical in shape to the original 201.

### 5. Telemetry
`scheduler.bookings` gains the outcomes `replayed` and `idempotency-key-reused`; the `scheduler.book` span gets `scheduler.idempotent=true` when a key is present.

## Risks / Trade-offs

- **[Global keys]** Two clients could collide on a non-random key → documented: use UUIDs (A16).
- **[No expiry]** Keys accumulate on rows; acceptable at this scale.

## Migration Plan

One additive migration (nullable column, unique index). Rollback drops both.

## Open Questions

None.
