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

## Working agreements

Agreed with the repository owner while building this project. They apply to every agent and session, including cloud sessions that do not have the owner's local settings.

- **Read first:** `project-mode.yaml`, `PROJECT_STATE.md` (decisions D1–D24 and current state) and `openspec/changes/<change>/tasks.md` before doing anything.
- **HYBRID mode** (`project-mode.yaml`): only feature code inside an approved task is done without asking. Tests, design, configuration (project files, packages, settings, Docker, CI) and documentation need the owner's permission for each change.
- **Process per task** (D17): present the approach → owner approves → write the feature code → write the tests → show real `dotnet build` / `dotnet test` output → owner approves → commit. One task per request.
- **Tests** (D15, D20): code first, tests after. Write tests only for scenarios the owner approved (S1–S31 in the change's `spec.md`, plus extra cases recorded in `PROJECT_STATE.md`). Propose missing tests; do not add them unasked.
- **Questions:** answer in words. Do not run demos, probes or other work nobody asked for.
- **Git** (D14): never commit to `main`. Ask before every commit, push, pull request and merge. One feature with its tests per commit, green. One pull request per OpenSpec change, merged with a merge commit (no squash). The public history is part of the assessment, so never rewrite pushed history.
- **Plan changes:** if a step is done differently from `tasks.md`, strike it through and write the reason next to it; record decisions and evidence in `PROJECT_STATE.md`.
- **Model split:** implementing an approved task (feature code and its approved tests) may be delegated to a Sonnet model, as a subagent or a separate session. The reviewing session (Opus) reads every changed file against the approved approach and scenarios, and reruns `dotnet build` and `dotnet test` itself before asking the owner to commit. Before a change is merged, an Opus session reviews the whole diff of its pull request.
- **Environment:** integration tests need Docker running (Testcontainers, PostgreSQL 18). If Docker is not available, say so and rely on CI; do not mark the task done without test evidence.

## Code style

- Nullable reference types enabled; file-scoped namespaces.
- Records for DTOs and value objects.
- Clear names over comments. No commented-out code, no placeholder comments.
- No abstractions without a current use (no speculative interfaces or base classes).
