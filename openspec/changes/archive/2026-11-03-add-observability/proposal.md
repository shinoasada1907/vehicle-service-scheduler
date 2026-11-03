## Why

The API works but cannot be observed: there are no traces, no business metrics, no health endpoints and no dashboard in the local run. Section 10 of `docs/system-design.md` defines what operators need to answer — is booking working, how fast, why are requests rejected, where is the time spent. This is the "should" item "observability working in the demo" of D8/D12, plus the production-readiness items the owner asked for on 2026-11-02 (health checks, deployment notes) and a configurable API port (`API_PORT`) because port 8080 is taken on the owner's machine.

## What Changes

- OpenTelemetry traces, metrics and logs exported over OTLP when `OTEL_EXPORTER_OTLP_ENDPOINT` is set: ASP.NET Core and Npgsql instrumentation, plus a `Scheduler` activity source and meter for the use cases.
- Business metrics: bookings by outcome, booking conflicts by resource, cancellations, reschedules by outcome.
- Use-case spans with outcome and attempts; conflict retries as span events.
- Structured logs of the key events (booking confirmed or rejected with reason, conflict retry, cancelled, rescheduled), identifiers only.
- `GET /health/live` and `GET /health/ready` (database reachable).
- Docker Compose gains the .NET Aspire Dashboard (UI on host port 18888) and `API_PORT` (default 8080); container logs as JSON.
- README: observability and "deploying to production" sections.

## Capabilities

### New Capabilities
- `observability`: health endpoints, telemetry export, business metrics, use-case traces and key-event logs.

### Modified Capabilities
None.

## Impact

- Application: an instrumentation class using `System.Diagnostics` (`ActivitySource`, `Meter`) only — no new package in Application.
- Api: OpenTelemetry and health-check registration, key-event logging, two endpoints.
- Packages (approved in D13): OpenTelemetry.Extensions.Hosting, OpenTelemetry.Exporter.OpenTelemetryProtocol, OpenTelemetry.Instrumentation.AspNetCore, Npgsql.OpenTelemetry, Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore.
- docker-compose.yml, .env.example, README.md, docs/system-design.md (health paths, API_PORT).
