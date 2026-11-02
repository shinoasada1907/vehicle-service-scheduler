## Context

The repository holds only documentation: `docs/system-design.md` (the System Design Document), `AGENTS.md` (rules for coding agents) and the README skeleton. This change creates the first code. The system-level decisions are already made in the SDD (layered modular monolith, PostgreSQL exclusion constraints as the referee, UTC with dealership time zones, Problem Details). The decisions below are the detailed design that the SDD left for implementation time. Design-session decisions D5–D7 and D11–D13 in `PROJECT_STATE.md` are the source for most of them.

Facts checked on this machine before planning, in throwaway projects:

- .NET SDK 10.0.401 creates `.slnx` solutions by default.
- `xunit.v3` 4.x runs on Microsoft.Testing.Platform. On the .NET 10 SDK, `dotnet test` refuses the old VSTest mode for such projects; `global.json` must select the Microsoft.Testing.Platform runner. A test project with zero tests makes `dotnet test` exit with code 8.
- `Testcontainers.PostgreSql` 4.16.0 marks `new PostgreSqlBuilder()` obsolete; `new PostgreSqlBuilder("postgres:18")` must be used (warnings are errors here).
- `dotnet ef` 10.0.14 accepts a class library as startup project when it references `Microsoft.EntityFrameworkCore.Design`; the project must be restored or built first. Owned collections with record types and backing fields generate the expected tables; `TimeOnly` maps to `time without time zone`.
- Docker Desktop was not running when the plan was written; integration tests need it.

## Goals / Non-Goals

**Goals:**
- Fulfil AC1–AC3 of the brief through `POST /appointments` and `GET /appointments/{id}`.
- Make double booking impossible for bays, technicians and vehicles, and prove it with a parallel-request test against a real PostgreSQL.
- Keep every business rule in the framework-free domain layer and cover it with fast unit tests.
- Run locally with one `docker compose up --build`, and in CI on every push.

**Non-Goals:**
- Cancellation, availability lookup, observability, idempotent booking (later changes).
- Authentication, master-data management APIs, technician shifts.

## Decisions

### 1. The domain chooses, the database arbitrates
The use case loads every bay and technician of the dealership plus the confirmed appointments that overlap the requested range, and the domain (`ResourceAllocator`) filters by compatibility, qualification and freedom, then picks the first by name. The insert is the moment of truth: the exclusion constraints reject it if a concurrent request won. On a bay or technician conflict the use case reloads and tries again, at most 3 attempts in total; on a vehicle conflict it stops at once. A PostgreSQL deadlock (`40P01`) between two inserts that conflict on several exclusion constraints is handled like a lost race: the store reports it, the change tracker is cleared, and the use case retries within the same three attempts. To make that rare, before inserting the store takes PostgreSQL advisory transaction locks for the chosen bay, technician and vehicle, always in that order, so requests for the same resource queue and an insert never waits on another uncommitted appointment; the exclusion constraints remain the referee and the deadlock handling stays as a safety net.
*Alternatives:* SQL-only candidate selection (rules would live in SQL and be duplicated by the availability feature); a per-dealership advisory lock (serialises all bookings and still checks before inserting). Both rejected in design part 1 (D5).

### 2. Load all bays and technicians, filter in the domain
A dealership has tens of bays and technicians at most, so loading them all is cheap. Filtering in C# keeps compatibility and qualification rules in one tested place instead of repeating them in queries.

### 3. Expected outcomes are values, not exceptions
`AllocationResult` and `BookAppointmentResult` are closed record hierarchies; `AddAppointmentResult` is an enum. Exceptions are kept for bugs and infrastructure failures, which the API turns into 500 Problem Details through the exception handler. Nothing is swallowed: an exclusion violation on an unknown constraint is rethrown.

### 4. Ports per use case
`IBookingStore` serves the booking use case (load references, load schedule, try to add). `IAppointmentQueries` serves reads. The GET endpoint calls `IAppointmentQueries` directly: a handler that only forwards the call would be an abstraction without a use (`AGENTS.md`).

### 5. The response is the application read model
`AppointmentDetails` (with dealership, service type, bay and technician summaries) is returned as the JSON body by both endpoints. A separate API DTO would be a field-by-field copy. Trade-off: the HTTP contract follows the application model; if they need to diverge later, an API record is added then.

### 6. Exclusion constraints in raw SQL, named in one place
EF Core cannot model exclusion constraints, so the initial migration adds them with `migrationBuilder.Sql`. The names live in `AppointmentConstraints`; `BookingStore` maps the violated constraint name to `BayTaken`, `TechnicianTaken` or `VehicleTaken`; the advisory locks of decision 1 live in `BookingStore` next to this mapping. Each constraint uses `tstzrange(start_at, end_at, '[)')` (half-open) and `WHERE (status = 'Confirmed')` so cancelled appointments never block (AGENTS.md rule 4). `btree_gist` is enabled through `HasPostgresExtension`. After a failed save the change tracker is cleared, otherwise the next attempt would try to insert the rejected entity again.

### 7. snake_case schema without an extra package
Tables are named explicitly with `ToTable`, and a short loop at the end of `OnModelCreating` converts every column name to snake_case. This keeps raw SQL readable and follows PostgreSQL convention without adding `EFCore.NamingConventions`, which is not in the approved package list.

### 8. Seed data in code, not in migrations
A `DatabaseSeeder` inserts two dealerships (London, `Europe/London`, with daylight saving; Saigon, `Asia/Ho_Chi_Minh`, without), four service types, six bays, five technicians, two customers and three vehicles, only when the database is empty. Fixed GUIDs in `SeedData` are shared by the `.http` file, the README and the integration tests. `HasData` was rejected because owned collections need anonymous-object seeding and the data would be harder to read and reuse.

### 9. Migrations and seeding at start-up behind a flag
`Database:MigrateOnStartup` is `false` in `appsettings.json` and `true` in `appsettings.Development.json` and in Docker Compose. Production would run migrations as a pipeline step (SDD §4.4). Integration tests migrate and seed in their fixture and run the API with the flag off.

### 10. The connection string is read lazily
`AddInfrastructure` takes a `Func<IServiceProvider, string>`; `Program` passes a function that reads `ConnectionStrings:Scheduler` from `IConfiguration` when the first `DbContext` is created. The test host can therefore supply the container's connection string through configuration, and the infrastructure project does not depend on configuration abstractions.

### 11. Manual request validation with a strict timestamp rule
`BookAppointmentRequest` has nullable fields and a `TryCreateCommand` method that returns per-field errors. `startAt` is a string that must end in `Z` or `±hh:mm` before it is parsed, because the default JSON binding silently interprets a timestamp without an offset in the server's local time zone. Built-in minimal-API validation was not used: it cannot express the offset rule, and the explicit code is easy to test.

### 12. Problem Details conventions
`type` is `urn:vehicle-service-scheduler:problem:<slug>` (a URN avoids inventing a web domain), with slugs `validation`, `not-found`, `vehicle-not-owned`, `invalid-start-time`, `outside-opening-hours`, `service-not-offered`, `no-capacity`, `vehicle-already-booked`, `method-not-allowed`, `unsupported-media-type`, `internal-error`. Problem Details generated by the framework (malformed body, unknown route, wrong method or media type, unhandled exceptions) are normalised to the same format through `CustomizeProblemDetails`, and `internal-error` never exposes exception details. Every problem carries `traceId` (the current trace id, or the request's trace identifier when no trace is active), added explicitly so that it does not depend on framework defaults. `no-capacity` carries `shortage`.

### 13. Identifiers and time
Appointment ids are `Guid.CreateVersion7` (time-ordered, index friendly). The current time comes from an injected `TimeProvider`; tests use `FakeTimeProvider`. `TimeRange` refuses non-UTC bounds, so a local offset can never reach the database (Npgsql also requires UTC for `timestamptz`).

### 14. Build and test set-up
Central package management (`Directory.Packages.props`) pins the approved versions; `Directory.Build.props` sets `net10.0`, nullable, implicit usings and warnings as errors. `global.json` pins the SDK feature band (`10.0.100`, `latestFeature`) and selects the Microsoft.Testing.Platform runner. Because of that runner, `Microsoft.NET.Test.Sdk` and `xunit.runner.visualstudio` (approved in D13) are not needed and are not added. A test project is created only in the task that adds its first test, because an empty test project fails `dotnet test`.

### 15. Integration test set-up
One PostgreSQL 18 container per test run (xUnit collection fixture), migrated and seeded once. Each test class truncates `appointments` before every test; reference data is never modified, and tests that need extra vehicles create them with fresh ids. API tests use `WebApplicationFactory<Program>` with environment `Testing` and in-memory configuration. Booking dates are fixed in 2030 (2030-01-07 and 2030-07-01 are Mondays) so the real clock never makes them "past".

### 16. Branch and commits
Work happens on `feature/appointment-booking`, created from `docs/system-design` (which holds the SDD and this plan). One commit per task. Merging into `main` needs the user's approval.

## Risks / Trade-offs

- **[Docker not running]** Integration tests and Compose fail. → Start Docker Desktop before Task 5; the README states the prerequisite.
- **[The parallel test may not always produce a real race]** If requests happen to run one after another the test still passes without exercising the retry path. → The race path is also covered deterministically by unit tests (fake store reporting conflicts) and by a persistence test that triggers each exclusion constraint.
- **[Time zone data in the container]** `TimeZoneInfo.FindSystemTimeZoneById("Europe/London")` needs tzdata in the runtime image. → Task 7's functional check books a London appointment through the container.
- **[Records as EF owned types]** Value equality of records could confuse change tracking. → Owned entries are tracked by key; the persistence tests load and save dealerships and technicians with owned collections through the seeder.
- **[Response follows the application model]** See decision 5.

## Migration Plan

First schema; there is nothing to migrate from. Locally and in Compose the API applies migrations at start-up. Rollback is `docker compose down -v`, which removes the database volume.

## Open Questions

None blocking. Ports (8080 for the API) are confirmed in Task 7 and the README note is removed then.
