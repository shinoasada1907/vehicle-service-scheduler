# Vehicle Service Scheduler

Backend API for booking vehicle service appointments with real-time availability checks for service bays and qualified technicians.

Built for the **Keyloop Technical Assessment — Scenario A: The Unified Service Scheduler**.

<!-- TODO: add CI badge once .github/workflows/ci.yml exists -->

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
| Observability | Serilog + OpenTelemetry + Jaeger | Structured logs, metrics, distributed tracing |
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

<!-- TODO: confirm ports once implemented -->

| Service | URL |
| --- | --- |
| API | http://localhost:8080 |
| API docs (Scalar) | http://localhost:8080/scalar |
| Jaeger UI | http://localhost:16686 |

The database is migrated and seeded with sample dealerships, bays, technicians, customers and vehicles on startup.

### Run the API locally

```bash
docker compose up -d postgres jaeger
dotnet run --project src/Scheduler.Api
```

## Running tests

```bash
dotnet test
```

- **Unit tests** cover the core business rules (overlap detection, technician qualification, opening hours, allocation).
- **Integration tests** use Testcontainers to start a real PostgreSQL, so Docker must be running. They include a concurrency test: several parallel requests compete for the last free slot and exactly one must succeed.

## API usage

<!-- TODO: update request/response examples once the API is implemented -->

| Method | Path | Purpose |
| --- | --- | --- |
| GET | `/dealerships/{id}/availability?serviceTypeId=&date=` | List free time slots |
| POST | `/appointments` | Book an appointment (supports `Idempotency-Key`) |
| GET | `/appointments/{id}` | Get an appointment |
| POST | `/appointments/{id}/cancel` | Cancel an appointment |

Example:

```bash
curl -X POST http://localhost:8080/appointments \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: 7d1f0c2e-1b3a-4c5d-9e8f-0a1b2c3d4e5f" \
  -d '{
        "customerId": "<customer-id>",
        "vehicleId": "<vehicle-id>",
        "dealershipId": "<dealership-id>",
        "serviceTypeId": "<service-type-id>",
        "startAt": "2026-11-05T02:00:00Z"
      }'
```

More ready-to-run requests are in [`http/scheduler.http`](http/scheduler.http).

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

<!-- Write this section in your own words. Use concrete examples from docs/ai-log.md. -->

### Tools

<!-- Which AI tools you used and for what (design review, code generation, tests, docs). -->

### Strategy for guiding the AI

<!-- e.g. spec and test cases first, AGENTS.md rules, small tasks, one change per prompt. -->

### Verification and refinement

<!-- How you checked AI output: code review of every diff, tests, running the app, checking docs. -->

### Where the AI was wrong and how I caught it

<!-- 2–4 real examples from docs/ai-log.md: what it produced, how you noticed, what you changed. -->

### Ensuring final quality

<!-- Test coverage of core logic, CI, final manual review, what you rewrote by hand. -->

## Future improvements

<!-- e.g. authentication, notifications via outbox events, caching availability, multi-tenant partitioning, AWS deployment. -->