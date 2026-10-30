# PROJECT_STATE

## Project context

- Project/root: D:\Code\Product\vehicle-service-scheduler
- Last updated: 2026-10-30
- Active mode: AUTO
- Mode source: project-mode.yaml (selected by the user on 2026-10-30)
- Current phase: DESIGN
- Current task: TASK-001 Requirements analysis, system design and implementation plan for Scenario A
- Task owner: CLAUDE
- Reviewer: HUMAN
- Current state: PLANNED (implementation plan for add-appointment-booking committed at the user's request; execution approach not chosen yet)
- Branch: docs/system-design
- Files currently owned/being modified: PROJECT_STATE.md, openspec/changes/add-appointment-booking/**
- Other active agents: none recorded

## Task and file ownership

| Task | Owner | Reviewer | Mode | State | Exact files | Updated |
| --- | --- | --- | --- | --- | --- | --- |
| TASK-001 Design Scenario A | CLAUDE | HUMAN | AUTO | REVIEWING | PROJECT_STATE.md, openspec/changes/add-appointment-booking/** | 2026-10-30 |

## Completed work

- 391f697: README skeleton, AGENTS.md rules, AI collaboration log (docs only, no code).
- 2026-10-30: OpenSpec 1.6.0 initialized (`openspec init --tools claude`), creating openspec/ and .claude/{commands/opsx,skills/openspec-*}.
- 2026-10-30: docs/system-design.md draft 0.1 written (16 sections, 6 Mermaid diagrams), following the outline the user approved. Section 15 (GenAI) is a factual draft for the user to rewrite in their own words.
- 2026-10-30: OpenSpec change add-appointment-booking created: proposal.md, specs/appointment-booking/spec.md (11 requirements), design.md (16 decisions), tasks.md (7 tasks, 66 checkboxes, full code per step).

## Decisions recorded

- D1: Operating mode AUTO.
- D2: Use OpenSpec for change records; docs/system-design.md remains the submitted System Design Document.
- D3: Observability backend is the .NET Aspire Dashboard (OTLP traces, metrics, logs) instead of Jaeger, because Jaeger does not store metrics.
- D4: Assumptions A1–A12 from the requirements analysis approved by the user.
- D5: Design part 1 approved: booking approach A (Domain selects free bay + technician, the PostgreSQL exclusion constraint arbitrates, bounded retry on 23P01), four-layer architecture with Minimal API and per-use-case ports, data model with three exclusion constraints (bay, technician, vehicle).
- D6: Assumption A13 approved: a vehicle cannot have two overlapping active appointments, even at different dealerships.
- D7: Design part 2 approved: API contract and error mapping (400/404/422/409 with stable ProblemDetails types), booking flow with at most 3 attempts on bay/technician conflicts, availability computed in memory with the shared ResourceAllocator, idempotency checked against the stored appointment. Assumptions A14 (cancel only before start, repeat cancel is idempotent), A15 (availability date is the dealership's local date), A16 (Idempotency-Key is global while there is no authentication) approved.
- D8: Overall direction agreed. Work in three levels (brief and direction, then system design in the SDD, then detailed design and code per task). Scope: must = booking with bay + qualified technician check, get appointment, no double booking under concurrency, core tests, client stub, one-command run, SDD; should = availability lookup, cancel, observability in the demo, CI; could = Idempotency-Key; won't = auth, UI, notifications, rescheduling, technician shifts.
- D9: Assumption A17 (bays and technicians are independent pools; any technician can work in any bay of the same dealership) proposed; the user raised no objection. Rejections state whether bays or technicians were the bottleneck.
- D10: Plan-versus-reality handling (early finish, overrun, extra work, technician late or absent) is designed in a dedicated SDD section only; the code stays focused on booking (option A).
- D11: Approved by the user: logging uses built-in .NET structured logging exported through OpenTelemetry instead of Serilog (SDD section 9), to avoid an extra dependency and get log-trace correlation in the Aspire Dashboard.
- D12: Delivery split into four OpenSpec changes in MoSCoW order: add-appointment-booking (must), add-cancellation-and-availability (should), add-observability (should), add-idempotent-booking (could). Only the first has a detailed plan; the others are planned when their turn comes.
- D13: NuGet packages approved for the whole project: Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3, Microsoft.EntityFrameworkCore.Design 10.0.14 (+ dotnet-ef tool), Microsoft.AspNetCore.OpenApi 10.0.14, Scalar.AspNetCore 2.18.6, xunit.v3 4.0.2 (+ runner and Microsoft.NET.Test.Sdk, which turned out not to be needed with Microsoft.Testing.Platform), Testcontainers.PostgreSql 4.16.0, Microsoft.AspNetCore.Mvc.Testing 10.0.14, Microsoft.Extensions.TimeProvider.Testing 10.11.0; for add-observability: OpenTelemetry.Extensions.Hosting, OpenTelemetry.Exporter.OpenTelemetryProtocol, OpenTelemetry.Instrumentation.AspNetCore 1.20.1, Npgsql.OpenTelemetry 10.0.3, Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore 10.0.14.

## Architecture summary

Planned, not implemented (see docs/system-design.md): ASP.NET Core (.NET 10) modular monolith with Domain / Application / Infrastructure / Api layers, PostgreSQL with exclusion constraints preventing double booking for bays, technicians and vehicles, OpenTelemetry to the Aspire Dashboard, Docker Compose for local runs. No source code exists yet.

## Known issues

- README ports for the Aspire Dashboard (18888) and the API (8080) are planned values, still marked "confirm ports once implemented".
- Docker Desktop was not running on 2026-10-30; integration tests (task 5 onwards) and Docker Compose need it.

## Verification evidence

- Mermaid diagrams in docs/system-design.md: all 6 blocks rendered to SVG without errors with mermaid 11.4.1 in the built-in browser (a sequence-diagram parse error caused by a semicolon was found and fixed). Visual layout was NOT checked by screenshot (the screenshot timed out).
- Placeholder scan of the SDD: none found. Table-of-contents anchors match the 16 section headings.
- No project source code exists; no project build or test has been run. Environment observed: .NET SDK 10.0.401, Docker 29.8.0 (engine not running), OpenSpec CLI 1.6.0, Node 26.1.0.
- Throwaway probes in the session scratchpad (not in the repo) confirmed: `dotnet new sln` creates .slnx; xunit.v3 4.0.2 runs only with the Microsoft.Testing.Platform runner selected in global.json; a test project with zero tests exits with code 8; `new PostgreSqlBuilder()` is obsolete in Testcontainers 4.16.0 (use the image constructor); `dotnet ef` 10.0.14 works with a class library startup project after a build, and maps an owned record collection and TimeOnly as expected; Scalar.AspNetCore 2.18.6 compiles with `MapScalarApiReference()`.
- `openspec validate add-appointment-booking --strict`: valid; `openspec status`: 4/4 artifacts complete; apply parser: 66 tasks.

## Next task

Human reviews the plan in openspec/changes/add-appointment-booking/ and approves committing it on docs/system-design. Then choose the execution approach (subagent per task, or inline) and start task 1 on branch feature/appointment-booking.

## Last working checkpoint

Branch docs/system-design: commit "docs: add system design document and spec-driven workflow" (SDD draft 0.1, OpenSpec set-up, mode and state files). Not merged into main; merging needs the user's approval.
