# idempotent-booking Specification

## Purpose
Safe retries of `POST /appointments` with an optional `Idempotency-Key` header: a retry with the same key and data returns the original appointment, a reused key with different data is rejected, and concurrent duplicates create one appointment. Introduced by the change `add-idempotent-booking` (archived 2026-11-03).
## Requirements
### Requirement: Replay a booking with the same idempotency key
When `POST /appointments` carries an `Idempotency-Key` that was already used with the same customer, vehicle, dealership, service type and start time, the system SHALL return the original appointment with 201 and the same `Location`, and SHALL NOT create another appointment.

#### Scenario: I1 — Retry after success
- **WHEN** the same booking request is sent twice with the same `Idempotency-Key`
- **THEN** both responses are 201 with the same appointment id, and the database holds one appointment

#### Scenario: I2 — Retry after the slot changed
- **WHEN** a booking with a key succeeded and the same request is retried with the key
- **THEN** the original appointment is returned, even though its own booking now occupies the slot and the vehicle

#### Scenario: I3 — No header
- **WHEN** a booking is sent without the header
- **THEN** it behaves exactly as before

### Requirement: Reject a reused key with different data
When the key was already used with different request data, the system SHALL respond 422 with problem type `idempotency-key-reused`.

#### Scenario: I4 — Same key, different start
- **WHEN** a key used for a 09:00 booking is sent with a 10:00 booking
- **THEN** the system responds 422 with type `idempotency-key-reused`, and no appointment is created at 10:00

### Requirement: At most one appointment per key under concurrency
Concurrent requests with the same key SHALL create at most one appointment; the database SHALL enforce the uniqueness of the key.

#### Scenario: I5 — Parallel requests with the same key
- **WHEN** 5 identical booking requests with the same key are sent in parallel
- **THEN** all respond 201 with the same appointment id, and the database holds one appointment with that key

### Requirement: Validate the header
An `Idempotency-Key` that is empty, longer than 100 characters or contains characters outside visible ASCII SHALL be rejected with 400 `validation` and an error for `Idempotency-Key`.

#### Scenario: I6 — Invalid key
- **WHEN** the header is 101 characters long
- **THEN** the system responds 400 with type `validation` and an error for `Idempotency-Key`

