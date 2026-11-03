## Context

Two changes are merged: booking (with concurrency proof) and availability/rescheduling/cancellation. The SDD (sections 9, 10) already fixes the direction: OpenTelemetry over OTLP to the .NET Aspire Dashboard locally (D3), built-in .NET logging exported through OpenTelemetry instead of Serilog (D11), packages approved in D13. On 2026-11-03 the owner asked Claude to carry out this change end to end without further approval steps; the choices below were made by Claude on that basis and are recorded as D30.

## Goals / Non-Goals

**Goals:** the four questions of SDD §10 answerable from the dashboard; health endpoints for orchestrators; zero telemetry cost when no collector is configured; no personal data in telemetry; one command still runs everything.

**Non-Goals:** alerts (described in the SDD only), a production collector, authentication of the dashboard, tracing inside the test suite's assertions beyond the business instrumentation.

## Decisions

### 1. Instrumentation lives in Application, with BCL types only
A static `SchedulerTelemetry` class in `Scheduler.Application` owns one `ActivitySource` and one `Meter`, both named `Scheduler`. `System.Diagnostics` and `System.Diagnostics.Metrics` are part of the runtime, so Application still references Domain only and no package. Counters: `scheduler.bookings` (tag `outcome`: confirmed, no-capacity-bays, no-capacity-technicians, no-capacity-bays-and-technicians, vehicle-already-booked, rule-violated, not-found), `scheduler.booking.conflicts` (tag `resource`: bay, technician, deadlock), `scheduler.reschedules` (tag `outcome`), `scheduler.cancellations` (tag `outcome`: cancelled, already-cancelled, window-closed, not-found). *Alternative:* instrument in the Api from results; rejected for conflicts and attempts, which only the use case sees.

### 2. Use-case spans
Book, reschedule and cancel each start an activity (`scheduler.book`, `scheduler.reschedule`, `scheduler.cancel`) with tags `scheduler.dealership_id`, `scheduler.service_type_id` (when known), `scheduler.outcome` and `scheduler.attempts`; each lost race or deadlock adds a span event `conflict` with the resource. Activities are null when nobody listens, so the cost is negligible.

### 3. Logs from the Api, structured, identifiers only
The endpoints log one event per outcome with message templates (`Appointment {AppointmentId} confirmed …`, `Booking rejected: {Reason} …`, `Appointment {AppointmentId} cancelled`, `Appointment {AppointmentId} rescheduled …`); unhandled errors are already logged by the exception handler. No names, e-mails or VINs. Logging from the Api avoids adding the logging abstractions package to Application. Retries are visible as span events and the conflict counter.

### 4. Export only when configured
`builder.Services.AddOpenTelemetry()` with resource `service.name` from `OTEL_SERVICE_NAME` (default `scheduler-api`), tracing (ASP.NET Core, Npgsql, `Scheduler` source), metrics (ASP.NET Core, Npgsql, `Scheduler` meter) and logging; the OTLP exporter is added only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set. Tests and plain `dotnet run` export nothing.

### 5. Health endpoints
`/health/live` has no checks (process up). `/health/ready` runs the EF Core `DbContext` check (`AddDbContextCheck<SchedulerDbContext>`, tag `ready`). Both return the default plain-text status (200 Healthy, 503 Unhealthy) and are excluded from the OpenAPI document.

### 6. Compose
New service `aspire-dashboard` (`mcr.microsoft.com/dotnet/aspire-dashboard`, pinned tag), UI on host `${DASHBOARD_PORT:-18888}`, anonymous access for local use only; the API gets `OTEL_EXPORTER_OTLP_ENDPOINT=http://aspire-dashboard:18889`, `OTEL_SERVICE_NAME=scheduler-api` and `Logging__Console__FormatterName=json`. The API's host port becomes `${API_PORT:-8080}`. `.env.example` documents both ports.

### 7. Documentation
README: "Observability" (what to open, what each metric means) and "Deploying to production" (required environment variables, migrations as a separate pipeline step, never the development password, health paths, OTLP to a collector). SDD: health paths, `API_PORT`, the metric names.

## Risks / Trade-offs

- **[Package versions]** D13 fixed versions for some packages only; the nearest stable versions compatible with .NET 10 are chosen and recorded.
- **[Dashboard without authentication]** Local demo only, stated in the README and the Compose comment.
- **[Metric cardinality]** Tags are small fixed sets; no ids in metric tags.

## Migration Plan

No schema change. Rollback is removing the registration and the Compose service.

## Open Questions

None.
