# Vehicle Service Scheduler

Backend API for booking vehicle service appointments with real-time availability checks for service bays and qualified technicians.

Built for the **Keyloop Technical Assessment — Scenario A: The Unified Service Scheduler**.

[![CI](https://github.com/shinoasada1907/vehicle-service-scheduler/actions/workflows/ci.yml/badge.svg)](https://github.com/shinoasada1907/vehicle-service-scheduler/actions/workflows/ci.yml)

## Overview

- **Resource-constrained booking:** request an appointment for a specific vehicle, service type, dealership and start time.
- **Real-time availability check:** before confirming, the API verifies that a compatible service bay **and** a qualified technician are free for the entire service duration.
- **Confirmed appointment record:** on success, a persistent appointment links the customer, vehicle, technician and service bay.
- **No double booking:** guaranteed at the database level with a PostgreSQL exclusion constraint, so it stays correct under concurrent requests and multiple API instances.

## Tech stack

| Layer | Choice | Why |
| --- | --- | --- |
| API | ASP.NET Core (.NET 10 LTS) | Mature, fast, strong typing |
| Database | PostgreSQL 18 | Range types + exclusion constraints prevent overlapping bookings |
| Data access | Entity Framework Core | Migrations and LINQ; raw SQL only for the constraint |
| Testing | xUnit + Testcontainers | Integration tests against a real PostgreSQL |
| Observability | OpenTelemetry + .NET Aspire Dashboard | Structured logs, metrics and traces over OTLP; built-in .NET logging, no extra logging library |
| API docs | OpenAPI + Scalar UI | Interactive client stub |
| Runtime | Docker Compose | One command to run everything |

## Getting started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Docker (Docker Desktop or Docker Engine with Compose)

### Run everything with Docker Compose

```bash
docker compose up --build
```

| Service | URL |
| --- | --- |
| API | http://localhost:8080 |
| API docs (Scalar) | http://localhost:8080/scalar/ |
| Aspire Dashboard (logs, traces, metrics) | http://localhost:18888 |

The database is migrated and seeded with sample dealerships, bays, technicians, customers and vehicles on startup.

The API port can be changed with `API_PORT` and the dashboard port with `DASHBOARD_PORT`, both in `.env` (see `.env.example`). This helps when another application already uses port 8080.

### Run the API locally

```bash
docker compose up -d postgres
dotnet run --project src/Scheduler.Api
```

In Visual Studio: set `Scheduler.Api` as the startup project, start PostgreSQL first with `docker compose up -d postgres`, then press F5. The `http` launch profile opens Scalar.

PostgreSQL is exposed on host port 5433 by default. To change it, set `POSTGRES_PORT` in `.env` and update `ConnectionStrings__Scheduler` to the same port.

## Running tests

```bash
dotnet test
```

- **Unit tests** (67) cover the core business rules (overlap detection, technician qualification, opening hours, allocation, change cut-off, availability).
- **Integration tests** (63) use Testcontainers to start a real PostgreSQL, so Docker must be running. They include two concurrency tests. In the first, 12 parallel bookings compete for the London dealership's two general bays, and exactly 2 succeed while the other 10 receive `409 Conflict`. In the second, a reschedule and a new booking race for the last free time, and exactly one wins.

## API usage

| Method | Path | Purpose |
| --- | --- | --- |
| POST | `/appointments` | Book an appointment; optional `Idempotency-Key` header for safe retries |
| GET | `/appointments/{id}` | Get an appointment |
| GET | `/dealerships/{id}/availability?serviceTypeId=&date=` | List the free start times for a service on a day (`date` is `yyyy-MM-dd`, in the dealership's local time) |
| GET | `/appointments/{id}/alternatives?date=` | List free start times for an existing appointment's service (`date` is optional) |
| POST | `/appointments/{id}/reschedule` | Move an appointment to a new `startAt` |
| POST | `/appointments/{id}/cancel` | Cancel an appointment |

Every error is returned as RFC 7807 Problem Details with a `traceId`. The `type` is `urn:vehicle-service-scheduler:problem:<slug>`:

| Status | Problem type slug | Meaning |
| --- | --- | --- |
| 400 | `validation` | A field is missing or invalid (for example `startAt` without a UTC offset) |
| 404 | `not-found` | The dealership, service type, customer, vehicle or appointment does not exist |
| 422 | `vehicle-not-owned` | The vehicle does not belong to the customer |
| 422 | `invalid-start-time` | The start is in the past or not on a 15-minute boundary in the dealership's local time |
| 422 | `outside-opening-hours` | The whole service does not fit within the dealership's opening hours on one day |
| 422 | `service-not-offered` | The dealership cannot perform this service type |
| 422 | `change-window-closed` | The appointment starts within the change cut-off (2 hours by default), so it can no longer be rescheduled or cancelled |
| 422 | `appointment-cancelled` | The appointment is cancelled, so it can no longer be rescheduled and has no alternatives |
| 422 | `idempotency-key-reused` | The `Idempotency-Key` was already used with different request data |
| 409 | `no-capacity` | No compatible bay and qualified technician are free; `shortage` is `bays`, `technicians` or `bays-and-technicians` |
| 409 | `vehicle-already-booked` | The vehicle already has an overlapping confirmed appointment |

**Safe retries.** Send a UUID in the `Idempotency-Key` header of `POST /appointments`. A retry with the same key and the same data returns the original appointment with `201 Created` and the same `Location`, and creates nothing new. The same key with different data returns `422` `idempotency-key-reused`. Keys are global for now (there is no authentication) and do not expire. Concurrent duplicates create exactly one appointment. A key must be 1 to 100 visible ASCII characters, otherwise the response is `400` `validation`.

Example (ids come from [Sample data](#sample-data)):

```bash
curl -X POST http://localhost:8080/appointments \
  -H "Content-Type: application/json" \
  -d '{
        "customerId": "c0000000-0000-4000-8000-000000000001",
        "vehicleId": "ca000000-0000-4000-8000-000000000001",
        "dealershipId": "d0000000-0000-4000-8000-000000000001",
        "serviceTypeId": "5e000000-0000-4000-8000-000000000001",
        "startAt": "2030-01-07T09:00:00Z"
      }'
```

Response `201 Created`, with a `Location` header pointing to the new appointment:

```json
{
  "id": "01a19b34-c4b8-7804-a806-6ff79194872d",
  "status": "Confirmed",
  "customerId": "c0000000-0000-4000-8000-000000000001",
  "vehicleId": "ca000000-0000-4000-8000-000000000001",
  "dealership": {
    "id": "d0000000-0000-4000-8000-000000000001",
    "name": "Riverside Motors London",
    "timeZone": "Europe/London"
  },
  "serviceType": {
    "id": "5e000000-0000-4000-8000-000000000001",
    "name": "Oil change",
    "durationMinutes": 60
  },
  "serviceBay": { "id": "ba000000-0000-4000-8000-000000000001", "name": "London Bay 1" },
  "technician": { "id": "7e000000-0000-4000-8000-000000000001", "name": "Alice Walker" },
  "startAt": "2030-01-07T09:00:00+00:00",
  "endAt": "2030-01-07T10:00:00+00:00",
  "createdAt": "2026-11-02T07:41:29.1443702+00:00"
}
```

### Changing an appointment

The recommended flow is to look at the free start times first, then reschedule to one of them, or cancel:

```bash
curl "http://localhost:8080/dealerships/d0000000-0000-4000-8000-000000000001/availability?serviceTypeId=5e000000-0000-4000-8000-000000000001&date=2030-01-14"
```

```json
{
  "dealershipId": "d0000000-0000-4000-8000-000000000001",
  "serviceTypeId": "5e000000-0000-4000-8000-000000000001",
  "date": "2030-01-14",
  "timeZone": "Europe/London",
  "slots": [
    { "startAt": "2030-01-14T08:00:00+00:00", "endAt": "2030-01-14T09:00:00+00:00" },
    { "startAt": "2030-01-14T08:15:00+00:00", "endAt": "2030-01-14T09:15:00+00:00" }
  ]
}
```

The real response for this day lists 37 start times, from 08:00 to 17:00 local time.

- `GET /appointments/{id}/alternatives` returns the same shape for an existing appointment. Its own time counts as free, so it can be moved by a few minutes.
- `POST /appointments/{id}/reschedule` with `{ "startAt": "..." }` keeps the appointment id. The move is atomic: if the new time cannot be allocated, the appointment stays exactly where it was.
- An appointment can be changed or cancelled only more than 2 hours before it starts; later changes return `change-window-closed`. Change the cut-off with the `Scheduling:ChangeCutoff` setting, or the environment variable `Scheduling__ChangeCutoff` (for example `Scheduling__ChangeCutoff=04:00:00`).
- `POST /appointments/{id}/cancel` is safe to repeat: cancelling an already cancelled appointment returns `200 OK` with the same state. A cancelled appointment frees its bay and technician.

More ready-to-run requests, including every error case, are in [`http/scheduler.http`](http/scheduler.http).

## Observability

With Docker Compose the API sends logs, traces and metrics over OpenTelemetry (OTLP) to the Aspire Dashboard. Open http://localhost:18888 (or the port in `DASHBOARD_PORT`) after making a few requests:

- **Structured logs:** JSON on the console and in the dashboard, with the trace id of the request. They contain identifiers only, never names or contact details.
- **Traces:** one trace per request, with a span for each database call and a span for the use case: `scheduler.book`, `scheduler.reschedule` and `scheduler.cancel`. The use-case spans carry the dealership, service type, outcome and number of attempts; every lost race or deadlock adds a `conflict` event.
- **Business metrics** (meter `Scheduler`):

| Metric | Tag | Meaning |
| --- | --- | --- |
| `scheduler.bookings` | `outcome` | Booking requests by result: `confirmed`, `no-capacity-bays`, `no-capacity-technicians`, `no-capacity-bays-and-technicians`, `vehicle-already-booked`, `rule-violated`, `not-found` |
| `scheduler.booking.conflicts` | `resource` | Insert attempts lost to a concurrent request: `bay`, `technician`, `deadlock` |
| `scheduler.reschedules` | `outcome` | Reschedule requests by result |
| `scheduler.cancellations` | `outcome` | Cancellations by result: `cancelled`, `already-cancelled`, `window-closed`, `not-found` |

- **Health endpoints:** `GET /health/live` answers while the process is running; `GET /health/ready` also checks the database. Both return `200` when healthy and `503` otherwise.

The API exports telemetry only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set. Docker Compose sets it, so tests and a plain `dotnet run` export nothing.

## Deploying to production

The Compose file is for local use. For a real environment:

- **Environment variables:**
  - `ConnectionStrings__Scheduler`: the production database, with its own credentials. Never use the development password.
  - `OTEL_EXPORTER_OTLP_ENDPOINT`: an OpenTelemetry Collector, which forwards to the monitoring backend.
  - `OTEL_SERVICE_NAME`: the service name shown in the backend (for example `scheduler-api`).
  - `Scheduling__ChangeCutoff`: the change cut-off, for example `02:00:00`.
- **Migrations:** set `Database__MigrateOnStartup=false` and run migrations as a step in the delivery pipeline, with `dotnet ef database update` or a migration bundle, before the new version starts.
- **Probes:** use `/health/live` as the liveness probe and `/health/ready` as the readiness probe.
- **Dashboard:** the Aspire Dashboard runs anonymously and is for local use only. Do not deploy it.

## Sample data

The seeder creates this data on startup. The ids are fixed and shared by [`http/scheduler.http`](http/scheduler.http) and the tests.

**Dealerships**

| Name | Id | Time zone | Opening hours (local) |
| --- | --- | --- | --- |
| Riverside Motors London | `d0000000-0000-4000-8000-000000000001` | `Europe/London` | Mon-Fri 08:00-18:00, Sat 09:00-13:00 |
| Saigon Auto Service | `d0000000-0000-4000-8000-000000000002` | `Asia/Ho_Chi_Minh` | Mon-Sat 08:00-17:00 |

**Service types**

| Name | Id | Duration | Bay type |
| --- | --- | --- | --- |
| Oil change | `5e000000-0000-4000-8000-000000000001` | 60 min | General |
| Brake inspection | `5e000000-0000-4000-8000-000000000002` | 90 min | General |
| Wheel alignment | `5e000000-0000-4000-8000-000000000003` | 60 min | Alignment |
| EV battery check | `5e000000-0000-4000-8000-000000000004` | 120 min | Electric vehicle |

**Service bays**

| Name | Dealership | Type |
| --- | --- | --- |
| London Bay 1 | Riverside Motors London | General |
| London Bay 2 | Riverside Motors London | General |
| London Alignment Bay | Riverside Motors London | Alignment |
| London EV Bay | Riverside Motors London | Electric vehicle |
| Saigon Bay A | Saigon Auto Service | General |
| Saigon Bay B | Saigon Auto Service | General |

**Technicians**

| Name | Dealership | Qualifications |
| --- | --- | --- |
| Alice Walker | Riverside Motors London | Oil change, brake inspection, wheel alignment |
| Ben Carter | Riverside Motors London | Oil change, brake inspection |
| Chloe Evans | Riverside Motors London | Oil change, EV battery check |
| Tran Van Minh | Saigon Auto Service | Oil change, brake inspection |
| Le Thi Lan | Saigon Auto Service | Oil change |

**Customers**

| Name | Id |
| --- | --- |
| Jane Smith | `c0000000-0000-4000-8000-000000000001` |
| Nguyen Van An | `c0000000-0000-4000-8000-000000000002` |

**Vehicles**

| Vehicle | Id | Owner |
| --- | --- | --- |
| Volkswagen Golf | `ca000000-0000-4000-8000-000000000001` | Jane Smith |
| Tesla Model 3 | `ca000000-0000-4000-8000-000000000002` | Jane Smith |
| VinFast VF 8 | `ca000000-0000-4000-8000-000000000003` | Nguyen Van An |

## Project structure

```
├── src/
│   ├── Scheduler.Api/              # HTTP endpoints, validation, ProblemDetails
│   ├── Scheduler.Application/      # Use cases (booking, availability)
│   ├── Scheduler.Domain/           # Entities and business rules
│   └── Scheduler.Infrastructure/   # EF Core, PostgreSQL, migrations
├── tests/
│   ├── Scheduler.UnitTests/
│   └── Scheduler.IntegrationTests/
├── docs/
│   ├── system-design.md            # System Design Document
│   └── ai-log.md                   # Log of AI-assisted work and corrections
├── http/scheduler.http             # Client stub (REST requests)
├── docker-compose.yml
└── AGENTS.md                       # Rules and context for AI coding agents
```

## Design

The full System Design Document is in [`docs/system-design.md`](docs/system-design.md). It covers the architecture diagram, components, data flow, technology choices, observability and assumptions.

Key decisions:

- **Modular monolith** with clean layering: simple to build and test now, stateless so it scales horizontally.
- **Database-enforced consistency:** an exclusion constraint makes double booking impossible, even under race conditions.
- **Times stored in UTC**, with each dealership's local time zone applied at the edges.

## AI Collaboration Narrative

I built this project with AI as a team of agents and kept the decisions, the scenarios and the final check for myself. The full trail is in [`PROJECT_STATE.md`](PROJECT_STATE.md) (decisions D1–D31 with their evidence), the OpenSpec records in [`openspec/`](openspec/) and [`docs/ai-log.md`](docs/ai-log.md). I drafted this section with Claude Code from those records and then reviewed it.

### Tools

- **Claude (chat)** to analyse the brief and pick the scenario and stack.
- **Claude Code** for everything after that, in two roles: an **Opus** session as manager (requirements, design options, task plans, review, re-running build and tests, recording state) and a **Sonnet** implementer agent (code and tests for one approved task at a time, reporting back with real output).
- **OpenSpec** to keep one record per change: proposal, design decisions, the approved test scenarios and a task-level plan, archived into [`openspec/specs/`](openspec/specs/) when the change is merged.
- **Testcontainers, Docker Compose and GitHub Actions** as the independent judges of whether the code works.

### Strategy for guiding the AI

- **Rules first.** [`AGENTS.md`](AGENTS.md) fixes the non-negotiables before any code: UTC and half-open ranges, no check-then-insert (the database constraint is the referee), Problem Details for every error, `CancellationToken` everywhere, no new packages without approval.
- **Direction before detail.** The first design pass went straight to constraints and libraries. I stopped it and we agreed on the brief, scope and direction first, then the system design, then detailed design per task.
- **I own the test scenarios.** For each change I approved a plain list of scenarios (S1–S31 for booking, 29 for changes) before any test was written; tests cover exactly those, and anything extra needed my approval.
- **Small, verifiable steps.** One task per request, one green commit per task, one pull request per change, merged with a merge commit so the history shows the process.
- **A working mode that matches trust.** I started in AUTO mode and switched to HYBRID (D16) when the AI moved ahead of what we had agreed: code-level design before direction, a plan with pre-written code, demos nobody asked for. In HYBRID the AI writes approved feature code; design, tests, configuration and documentation need my permission. For the last two changes I delegated more (D30, D31) and had every choice recorded so I could check it.

### Verification and refinement

- The reviewing session reads every changed file against the approved approach and **re-runs `dotnet build` and `dotnet test` itself** before a commit; the implementer's report is never taken as proof.
- Concurrency is proven, not assumed: parallel-request tests in the suite, repeated many times, plus runs on freshly started Docker Compose stacks.
- Every request in [`http/scheduler.http`](http/scheduler.http) was sent in order against a fresh stack and compared with its expected status and problem type.
- The whole diff of each pull request is reviewed before merge, and CI must be green on the final commit and again on `main` after the merge.

### Where the AI was wrong and how I caught it

1. **Error format that broke the spec.** In the review of the first feature pull request, sending a malformed GUID returned a framework error with an RFC link as `type`, no `detail` and a different `traceId` format, which breaks scenario S31. All framework errors are now normalised to the project format (D25).
2. **500s under concurrency.** The parallel booking test failed now and then. Reproducing it on Docker Compose showed 2 of 15 rounds answering 500 instead of 409; the log said `40P01 deadlock detected`, which the code did not handle because it only expected `23P01` (D26). The first fix removed the 500s but revealed a second problem: on a freshly started stack only one of two free bays got booked, because repeated deadlocks used up every request's three attempts. Per-resource advisory locks taken in a fixed order removed the deadlocks entirely; 12 out of 12 rounds were then correct (D27).
3. **A red commit avoided.** Adding the `Cancelled` status changed the EF model without a migration, and 25 integration tests failed. The migration was moved into the same task so that every commit stays green.
4. **A race the first design missed.** The first version of idempotent booking returned 409 to some of five parallel identical requests, because PostgreSQL can report a bay, technician or vehicle conflict before the key conflict. The replay check now runs on those paths too.
5. **Process slips.** The AI once delegated with a model alias that pointed to an older model than intended, and an implementer started Docker Desktop on its own. I tightened the agent definition (pinned model and effort) and the task briefs.

### Ensuring final quality

- **130 tests** (67 unit, 63 integration against real PostgreSQL), including three concurrency proofs; CI runs them on every push and pull request.
- Every scenario I approved maps to at least one test; the mapping is in each archived `tasks.md`.
- Decisions, deviations from the plan (struck through with the reason) and verification evidence are recorded in `PROJECT_STATE.md` and the OpenSpec records, so the reasoning survives after the code.
- Final manual check: the whole stack started from `main` with one command, health checks green, the dashboard receiving traces, metrics and logs.
## Future improvements

- **Authentication and authorisation** (OAuth 2.0 / OpenID Connect): advisors limited to their dealership, idempotency keys scoped per client.
- **Notifications** through an outbox (confirmation, reminder, change).
- **Technician shifts and leave**, and tracking jobs that finish early or late with reassignment (system design, section 12).
- **Smarter allocation**: balance load, keep versatile technicians free for jobs only they can do.
- **Scale-out**: availability caching, read replicas, partitioning by dealership.
- **Cloud deployment** with infrastructure as code (for example AWS ECS, RDS and CloudWatch/X-Ray via an OpenTelemetry Collector).