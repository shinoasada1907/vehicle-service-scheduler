# AGENTS.md

Context and rules for AI coding agents working on this repository.

## Project

Backend API for booking vehicle service appointments (Keyloop assessment, Scenario A).
A booking must reserve one compatible service bay and one qualified technician for the whole service duration.
Only the backend is implemented. The client is stubbed with OpenAPI/Scalar and `http/scheduler.http`.

## Architecture

- `Scheduler.Domain`: entities and business rules. No dependencies on other projects or on EF Core.
- `Scheduler.Application`: use cases. Depends on Domain only. Defines interfaces for persistence.
- `Scheduler.Infrastructure`: EF Core, PostgreSQL, migrations. Implements Application interfaces.
- `Scheduler.Api`: HTTP endpoints, request validation, ProblemDetails. Wires everything together.
- Dependencies point inward only. Never reference Infrastructure from Domain or Application.

## Non-negotiable rules

1. **Time:** store and compare all times in UTC (`DateTimeOffset`, PostgreSQL `timestamptz`). Never use `DateTime.Now`. Inject `TimeProvider` where the current time is needed.
2. **Intervals are half-open** `[start, end)`. Two bookings overlap when `a.Start < b.End && a.End > b.Start`. Back-to-back bookings (one ends 10:00, next starts 10:00) are allowed.
3. **Never check-then-insert** to prevent double booking. The database exclusion constraint is the source of truth. Handle PostgreSQL error `23P01` (exclusion violation) by trying the next candidate or returning `409 Conflict`.
4. The exclusion constraint applies only to active appointments (cancelled appointments must not block a slot).
5. **Errors** are returned as RFC 7807 ProblemDetails. Do not swallow exceptions.
6. **Async all the way**, and pass `CancellationToken` through every async call.
7. **No secrets in code.** Configuration comes from environment variables or `appsettings` with `.env.example`.

## Libraries

- Do not add NuGet packages without asking first.
- Do not use MediatR, AutoMapper or FluentAssertions (license changes). Use plain services, manual mapping and xUnit asserts.

## Testing

- Business rules in Domain and Application must have unit tests.
- Database behaviour (constraint, concurrency) is covered by integration tests with Testcontainers.
- When the human provides test cases, implement them exactly. Never change or delete an existing test to make it pass without saying so explicitly.
- Run `dotnet build` and `dotnet test` before reporting a task as done.

## Workflow

- Work in small steps: one task per request.
- For changes touching more than a few files, describe the plan first and wait for approval.
- At the end of each task, list the files you changed and anything you were unsure about.
- If a requirement is unclear, ask. Do not invent behaviour; assumptions belong in `docs/system-design.md`.

## Code style

- Nullable reference types enabled; file-scoped namespaces.
- Records for DTOs and value objects.
- Clear names over comments. No commented-out code, no placeholder comments.
- No abstractions without a current use (no speculative interfaces or base classes).
