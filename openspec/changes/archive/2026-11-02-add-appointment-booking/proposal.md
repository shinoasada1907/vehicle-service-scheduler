## Why

Dealership service departments book workshop appointments by hand, which leads to two cars booked on one lift, jobs booked when no qualified technician is free, and idle capacity. This change delivers the core of Scenario A (the "Must" scope in `docs/system-design.md` §2): a caller can book an appointment that is confirmed only when a compatible service bay and a qualified technician are both free for the whole service duration, the appointment is stored persistently, and double booking is impossible even under concurrent requests.

## What Changes

- New .NET 10 solution with four layers (`Scheduler.Domain`, `Scheduler.Application`, `Scheduler.Infrastructure`, `Scheduler.Api`) and two test projects, built and tested in GitHub Actions on every push.
- Domain rules: half-open time ranges, dealership opening hours in the dealership's time zone, the 15-minute start grid, vehicle ownership, bay compatibility, technician qualification, and deterministic choice of a free bay and technician.
- Booking use case that reloads and retries (at most three attempts) when a concurrent request takes the chosen bay or technician.
- PostgreSQL schema with three exclusion constraints (bay, technician, vehicle) over confirmed appointments, plus seed data for two dealerships in different time zones.
- HTTP API: `POST /appointments` and `GET /appointments/{id}`, errors as RFC 7807 Problem Details, OpenAPI document and Scalar UI.
- Local run with `docker compose up --build` (API + PostgreSQL), a `.http` client stub, and README build/run/test instructions.

Out of scope here, planned as separate changes: cancellation and availability lookup (`add-cancellation-and-availability`), observability (`add-observability`), idempotent booking (`add-idempotent-booking`).

## Capabilities

### New Capabilities

- `appointment-booking`: booking a service appointment with bay and technician allocation for the whole duration, the booking rules, error responses, retrieval of an appointment, and the no-double-booking guarantee.

### Modified Capabilities

None. There are no existing specs.

## Impact

- **Code:** new `src/` and `tests/` trees; new root files `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `VehicleServiceScheduler.slnx`, `.config/dotnet-tools.json`, `Dockerfile`, `.dockerignore`, `docker-compose.yml`, `.env.example`, `.github/workflows/ci.yml`, `http/scheduler.http`; README sections for running and testing.
- **Dependencies (approved by the user, decision D13):** Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3, Microsoft.EntityFrameworkCore.Design 10.0.14 and the `dotnet-ef` 10.0.14 tool, Microsoft.AspNetCore.OpenApi 10.0.14, Scalar.AspNetCore 2.18.6, xunit.v3 4.0.2, Testcontainers.PostgreSql 4.16.0, Microsoft.AspNetCore.Mvc.Testing 10.0.14, Microsoft.Extensions.TimeProvider.Testing 10.11.0.
- **Runtime:** PostgreSQL 18 with the `btree_gist` extension. Docker must be running for integration tests and for `docker compose`.
- **API:** two new endpoints; no existing clients.
