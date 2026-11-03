# observability Specification

## Purpose
Making the scheduler observable and operable: health endpoints for orchestrators, OpenTelemetry traces, metrics and logs exported over OTLP when configured, business metrics and use-case spans for booking, rescheduling and cancellation, and the Aspire Dashboard in the local run. Introduced by the change `add-observability` (archived 2026-11-03).
## Requirements
### Requirement: Health endpoints
The system SHALL expose `GET /health/live`, which reports that the process is running, and `GET /health/ready`, which also checks that the database is reachable. Healthy SHALL return 200 and unhealthy 503.

#### Scenario: O1 — Liveness
- **WHEN** `/health/live` is requested
- **THEN** the system responds 200

#### Scenario: O2 — Readiness with the database up
- **WHEN** `/health/ready` is requested and the database is reachable
- **THEN** the system responds 200

#### Scenario: O3 — Readiness with the database down
- **WHEN** `/health/ready` is requested and the database cannot be reached
- **THEN** the system responds 503, while `/health/live` still responds 200

### Requirement: Business metrics
The system SHALL count bookings by outcome, booking conflicts by resource, reschedules by outcome and cancellations by outcome on a meter named `Scheduler`, with tags drawn from small fixed sets and no identifiers.

#### Scenario: O4 — Bookings by outcome
- **WHEN** one booking is confirmed and another is rejected because every compatible bay is busy
- **THEN** `scheduler.bookings` is incremented once with `outcome=confirmed` and once with `outcome=no-capacity-bays`

#### Scenario: O5 — Conflicts by resource
- **WHEN** a booking loses a race for a bay and then succeeds with another bay
- **THEN** `scheduler.booking.conflicts` is incremented with `resource=bay`

#### Scenario: O6 — Cancellations and reschedules
- **WHEN** an appointment is cancelled, and another is rescheduled
- **THEN** `scheduler.cancellations` is incremented with `outcome=cancelled` and `scheduler.reschedules` with `outcome=rescheduled`

### Requirement: Use-case traces
Booking, rescheduling and cancelling SHALL each produce an activity from the `Scheduler` source carrying the outcome and, for booking and rescheduling, the number of attempts; every lost race or deadlock SHALL add a `conflict` event naming the resource.

#### Scenario: O7 — Booking span
- **WHEN** a booking loses one race for a bay and then succeeds
- **THEN** a `scheduler.book` activity is recorded with `scheduler.outcome=confirmed`, `scheduler.attempts=2` and one `conflict` event with `resource=bay`

### Requirement: Telemetry export in the local run
When `OTEL_EXPORTER_OTLP_ENDPOINT` is configured, traces, metrics and logs SHALL be exported over OTLP; when it is not configured nothing SHALL be exported. Docker Compose SHALL start the .NET Aspire Dashboard and point the API at it, and SHALL let the API host port be changed with `API_PORT`.

#### Scenario: O8 — Dashboard shows the API
- **WHEN** the stack is started with Docker Compose and a booking is made
- **THEN** the dashboard lists the `scheduler-api` resource with the booking's trace, the `scheduler.bookings` metric and the structured log entries

