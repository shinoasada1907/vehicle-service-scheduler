# Implementation Plan — add-appointment-booking

Task-level plan, approved by the user at stage 5 of the agreed process (decision D17 in `PROJECT_STATE.md`). It lists the work only; no code is written in advance. It replaces the earlier draft with pre-written code, which stays in history in commit 6aed191 (D18).

## How every task runs

1. Claude presents the approach for the task, and the user approves it.
2. Claude writes the feature code.
3. Claude writes tests for exactly the scenarios listed for the task (S1–S31 in `specs/appointment-booking/spec.md`). Code comes first, tests after (D15). Any test outside the list needs the user's approval.
4. Claude shows real build and test output, and the user approves.
5. Commit: one feature with its tests, all green (D14). Claude then asks before pushing.

Steps marked 🔒 change configuration or documentation: Claude asks for permission for that specific change before making it (`project-mode.yaml`).

If a step is done differently from this plan, strike it through and write the reason next to it.

## Constraints

- .NET 10, only the approved packages (D13), tests on xUnit v3 with Microsoft.Testing.Platform.
- The rules in `AGENTS.md` apply: UTC times, half-open ranges, the database constraint as the guarantee against double booking, Problem Details for errors, async with `CancellationToken`, no secrets in code.
- Work on branch `feature/appointment-booking`, created from `docs/system-design`. Never commit to `main`.
- A test project is created only in the task that adds its first test, because an empty test project makes `dotnet test` fail.
- Docker must be running from task 6 onwards.

## 1. Solution skeleton 🔒

- [x] 1.1 Present the approach and get approval, including permission for the configuration files
- [x] 1.2 Create branch `feature/appointment-booking` from `docs/system-design`
- [x] 1.3 ~~Create the solution, the four `src` projects with inward-only references, shared build settings, central package versions and `global.json`~~ Create the solution, the three `src` projects `Scheduler.Domain`, `Scheduler.Application` and `Scheduler.Infrastructure` with inward-only references, shared build settings, central package versions and `global.json`. **Reason:** a web project does not build without `Program.cs`, and a placeholder is not allowed (`AGENTS.md`), so `Scheduler.Api` is created in task 7 (approved by the user on 2026-11-01).
- [x] 1.4 Verify that `dotnet build` succeeds with no warnings, and get approval
- [x] 1.5 Commit `chore: set up solution and build settings`, then ask before pushing

## 2. Time and booking rules

- [x] 2.1 Present the approach and get approval
- [x] 2.2 Implement the half-open UTC time range, dealership opening hours in the dealership's time zone, customer and vehicle, and the booking policy (vehicle ownership, start in the future, 15-minute boundary, within opening hours)
- [x] 2.3 🔒 Create the unit test project
- [x] 2.4 Write tests for S16, S17, S18, S19, S20, S21
- [x] 2.5 Verify build and tests are green, and get approval
- [x] 2.6 Commit `feat(domain): add time and booking rules`, then ask before pushing

## 3. Continuous integration 🔒

- [x] 3.1 Present the workflow and get permission
- [x] 3.2 Add a GitHub Actions workflow that restores, builds and tests on every push and pull request
- [x] 3.3 Verify the same commands pass locally, and get approval
- [x] 3.4 Commit `ci: build and test on every push`, then ask before pushing
- [x] 3.5 After the push, check that the CI run is green

## 4. Bay and technician matching

- [x] 4.1 Present the approach and get approval
- [x] 4.2 Implement service bays, technicians and their qualifications, service types, appointments, and the allocator that picks the first free compatible bay and the first free qualified technician by name, or reports that the service is not offered or which resource is short
- [x] 4.3 Write tests for S5, S6, S7, S8, S9, S10, S11, S12, S13, S14, S15
- [x] 4.4 Verify build and tests are green, and get approval
- [x] 4.5 Commit `feat(domain): match a free bay and technician`, then ask before pushing

## 5. Booking use case

- [x] 5.1 Present the approach and get approval
- [x] 5.2 Implement the application layer: persistence ports, the booking use case (load references, apply the rules, allocate, store) with up to three attempts when a concurrent request takes the bay or technician, and the appointment read model
- [x] 5.3 Write tests, using an in-memory store and a controllable clock, for S1, S2, S3, S22, S27, S28, S30
- [x] 5.4 Verify build and tests are green, and get approval
- [x] 5.5 Commit `feat(application): add the booking use case`, then ask before pushing

## 6. PostgreSQL persistence

- [x] 6.1 Present the approach and get approval; check that Docker is running
- [x] 6.2 ~~Implement the EF Core mapping, the initial migration with the three exclusion constraints (bay, technician, vehicle; confirmed appointments only; half-open ranges), the booking store, the appointment query and the seed data~~ Implement the EF Core mapping, the initial migration with the three exclusion constraints (bay, technician, vehicle; confirmed appointments only; half-open ranges), the booking store and the seed data. **Reason:** the appointment query and the service registration are first used by the API in task 7 and tested there (S4, S25), so they move to task 7 to avoid code without a current use (approved by the user on 2026-11-01).
- [x] 6.3 🔒 Create the integration test project (PostgreSQL through Testcontainers) and add the `dotnet-ef` local tool
- [x] 6.4 Write database-level tests for S8, S29, S30, plus E1 (after a rejected insert the same store can add another appointment, the database side of S27) and E2 (the schedule holds only the dealership's bays, technicians and appointments, and the vehicle's appointments at any dealership), both approved by the user on 2026-11-01
- [x] 6.5 Verify build and tests are green, and get approval
- [x] 6.6 Commit `feat(infrastructure): store appointments in PostgreSQL`, then ask before pushing

## 7. HTTP API

- [x] 7.1 Present the approach and get approval, including permission for the configuration files in 7.3
- [x] 7.2 Implement `POST /appointments` and `GET /appointments/{id}`, request validation (an explicit UTC offset is required), Problem Details for every error, the OpenAPI document and the Scalar UI, plus the appointment query and the infrastructure service registration moved from task 6
- [x] 7.3 🔒 Create the `Scheduler.Api` project (moved from task 1), and add `appsettings` and launch settings (development-only connection string, migrate-and-seed flag)
- [x] 7.4 Write HTTP-level tests for S1, S3, S4, S11, S14, S16, S17, S19, S22, S23, S24, S25, S30, S31, plus E3 (the OpenAPI document is served and lists `/appointments`) and E4 (the Scalar UI is served), both approved by the user on 2026-11-01
- [x] 7.5 Verify build and tests are green, and get approval
- [x] 7.6 Commit `feat(api): add booking and retrieval endpoints`, then ask before pushing

## 8. Proof of no double booking

- [ ] 8.1 Present the approach and get approval
- [ ] 8.2 Write the parallel-request test for S26
- [ ] 8.3 Verify the test is green, and get approval
- [ ] 8.4 Commit `test: prove no double booking under parallel requests`, then ask before pushing

## 9. Docker Compose 🔒

- [ ] 9.1 Present the files and get permission
- [ ] 9.2 Add `Dockerfile`, `.dockerignore`, `docker-compose.yml` (API and PostgreSQL) and `.env.example`
- [ ] 9.3 Verify by hand with curl: book, read back, a London summer-time booking (time zone data in the container) and the Scalar UI; then `docker compose down -v`; get approval
- [ ] 9.4 Commit `build: run the API and PostgreSQL with Docker Compose`, then ask before pushing

## 10. Client stub and README 🔒

- [ ] 10.1 Present the changes and get permission
- [ ] 10.2 Add `http/scheduler.http` with the main success and error cases, using the seed data
- [ ] 10.3 Update the README sections on running, testing, API usage and sample data; the AI Collaboration Narrative stays the user's
- [ ] 10.4 Get approval
- [ ] 10.5 Commit `docs: add client stub and run instructions`, then ask before pushing

## 11. Close the change

- [ ] 11.1 If `docs/system-design` is not merged yet, its pull request goes first; ask the user before opening either pull request
- [ ] 11.2 Open the pull request for `feature/appointment-booking` (summary, scenarios covered, test evidence, notes on AI use) after the user's approval; CI must be green
- [ ] 11.3 Merge with a merge commit (no squash) after the user's approval
- [ ] 11.4 Archive with `openspec archive add-appointment-booking -y` after the user's approval, and update `PROJECT_STATE.md`
