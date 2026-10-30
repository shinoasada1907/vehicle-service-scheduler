# Appointment Booking Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task by task. Steps use checkbox (`- [ ]`) syntax; tick each one in this file when it is done. If a step has to be done differently, strike the old text through and write the reason next to it.

**Goal:** Book a vehicle service appointment through a REST API that confirms it only when a compatible bay and a qualified technician are free for the whole duration, stores it in PostgreSQL, and can never double book a bay, technician or vehicle.

**Architecture:** Four-layer .NET 10 modular monolith. The framework-free domain decides which bay and technician are free; PostgreSQL exclusion constraints are the final referee under concurrency; the booking use case retries up to three times when it loses a race. Minimal API endpoints map use-case results to HTTP status codes and Problem Details.

**Tech Stack:** .NET 10, ASP.NET Core Minimal APIs, EF Core 10 + Npgsql, PostgreSQL 18 (`btree_gist`), OpenAPI + Scalar, xUnit v3 on Microsoft.Testing.Platform, Testcontainers, Docker Compose, GitHub Actions.

**Spec:** `specs/appointment-booking/spec.md` (requirements and scenarios) and `design.md` (decisions 1–16) in this change; `docs/system-design.md` for the system-level design. Read all three before starting.

## Global Constraints

- Target `net10.0`. `global.json` pins SDK `10.0.100` with `rollForward: latestFeature` and selects the `Microsoft.Testing.Platform` test runner.
- Nullable reference types on, implicit usings on, warnings are errors, file-scoped namespaces, records for DTOs and value objects.
- Package versions live only in `Directory.Packages.props`. Use only the approved packages (D13). Do not add any other package without asking the user.
- No MediatR, AutoMapper or FluentAssertions. Tests use xUnit `Assert` only.
- Times are `DateTimeOffset` in UTC and `timestamptz` in PostgreSQL. Never call `DateTime.Now` or `DateTimeOffset.Now`; inject `TimeProvider`.
- Time ranges are half-open: `a` and `b` overlap when `a.Start < b.End && b.Start < a.End`.
- The exclusion constraints are the guarantee against double booking; handle PostgreSQL error `23P01`. They apply only to `status = 'Confirmed'`.
- Errors are RFC 7807 Problem Details. Never swallow exceptions.
- Async all the way; pass a `CancellationToken` to every async call. Tests use `TestContext.Current.CancellationToken`.
- No secrets in code. Development-only database credentials live in `appsettings.Development.json` and as Compose defaults, overridable through environment variables.
- Dependencies point inward: Domain references nothing; Application references Domain; Infrastructure references Application; Api references Application and Infrastructure.
- No commented-out code, no placeholder comments, no abstractions without a current use.
- One commit per task on branch `feature/appointment-booking`. Never commit to `main`; merging needs the user's approval.

## File Structure

```
global.json                          SDK pin and test runner
Directory.Build.props                shared compiler settings
Directory.Packages.props             approved package versions
VehicleServiceScheduler.slnx         solution
dotnet-tools.json                    dotnet-ef local tool
Dockerfile, .dockerignore            API container image
docker-compose.yml, .env.example     local run: postgres + api
.github/workflows/ci.yml             build and test on every push
http/scheduler.http                  client stub
src/Scheduler.Domain/
  Scheduling/TimeRange.cs            half-open UTC range, overlap rule
  Scheduling/BookingPolicy.cs        ownership, future start, 15-minute grid, opening hours
  Scheduling/BookingRuleViolation.cs
  Scheduling/ResourceAllocator.cs    choose free compatible bay + qualified technician
  Scheduling/AllocationResult.cs
  Scheduling/CapacityShortage.cs
  Dealerships/Dealership.cs          time zone, opening hours, local-time checks
  Dealerships/OpeningHours.cs
  Resources/BayType.cs, ServiceBay.cs, Technician.cs, Qualification.cs
  Catalog/ServiceType.cs             duration and required bay type
  Customers/Customer.cs, Vehicle.cs
  Appointments/Appointment.cs, AppointmentStatus.cs
src/Scheduler.Application/
  Booking/BookAppointmentCommand.cs, BookAppointmentResult.cs, BookAppointmentHandler.cs
  Booking/IBookingStore.cs           port + BookingReferences, ResourceSchedule, AddAppointmentResult
  Appointments/AppointmentDetails.cs read model returned by the API
  Appointments/IAppointmentQueries.cs
src/Scheduler.Infrastructure/
  Persistence/SchedulerDbContext.cs, SchedulerDbContextFactory.cs, AppointmentConstraints.cs
  Persistence/BookingStore.cs, AppointmentQueries.cs
  Persistence/Migrations/            generated InitialSchema + exclusion constraints
  Seeding/SeedData.cs, DatabaseSeeder.cs
  DependencyInjection.cs
src/Scheduler.Api/
  Program.cs                         composition root
  Appointments/AppointmentEndpoints.cs, BookAppointmentRequest.cs
  Problems/ProblemResults.cs
  appsettings.json, appsettings.Development.json, Properties/launchSettings.json
tests/Scheduler.UnitTests/           domain and use-case tests (no infrastructure)
tests/Scheduler.IntegrationTests/    real PostgreSQL via Testcontainers, API via WebApplicationFactory
```

---

## 1. Solution skeleton, TimeRange and CI

**Files:**
- Create: `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `VehicleServiceScheduler.slnx`
- Create: `src/Scheduler.Domain/Scheduler.Domain.csproj`, `src/Scheduler.Domain/Scheduling/TimeRange.cs`
- Create: `tests/Scheduler.UnitTests/Scheduler.UnitTests.csproj`, `tests/Scheduler.UnitTests/Scheduling/TimeRangeTests.cs`
- Create: `.github/workflows/ci.yml`
- Modify: `PROJECT_STATE.md`

**Interfaces:**
- Produces: `Scheduler.Domain.Scheduling.TimeRange` (`readonly record struct`) with constructor `TimeRange(DateTimeOffset start, DateTimeOffset end)`, properties `Start`, `End`, `static TimeRange FromDuration(DateTimeOffset start, TimeSpan duration)`, `bool Overlaps(TimeRange other)`.

- [ ] 1.1 Create the branch and claim the task

```bash
git switch docs/system-design
git switch -c feature/appointment-booking
```

In `PROJECT_STATE.md`, set "Current task" to `TASK-002 add-appointment-booking`, "Current phase" to `IMPLEMENTATION`, and add a row to the ownership table: `| TASK-002 add-appointment-booking | <your agent: CLAUDE or CODEX> | HUMAN | AUTO | IMPLEMENTING | files of the task in progress | <today> |`. Keep every other row.

- [ ] 1.2 Create the build settings

`global.json`:

```json
{
  "sdk": {
    "version": "10.0.100",
    "rollForward": "latestFeature"
  },
  "test": {
    "runner": "Microsoft.Testing.Platform"
  }
}
```

`Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>
```

`Directory.Packages.props`:

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <PackageVersion Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.14" />
    <PackageVersion Include="Microsoft.AspNetCore.OpenApi" Version="10.0.14" />
    <PackageVersion Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.14" />
    <PackageVersion Include="Microsoft.Extensions.TimeProvider.Testing" Version="10.11.0" />
    <PackageVersion Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.0.3" />
    <PackageVersion Include="Scalar.AspNetCore" Version="2.18.6" />
    <PackageVersion Include="Testcontainers.PostgreSql" Version="4.16.0" />
    <PackageVersion Include="xunit.v3" Version="4.0.2" />
  </ItemGroup>
</Project>
```

- [ ] 1.3 Create the Domain and unit test projects and the solution

`src/Scheduler.Domain/Scheduler.Domain.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
</Project>
```

`tests/Scheduler.UnitTests/Scheduler.UnitTests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="xunit.v3" />
    <PackageReference Include="Microsoft.Extensions.TimeProvider.Testing" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\Scheduler.Domain\Scheduler.Domain.csproj" />
  </ItemGroup>
  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>
</Project>
```

```bash
dotnet new sln --name VehicleServiceScheduler
dotnet sln VehicleServiceScheduler.slnx add src/Scheduler.Domain/Scheduler.Domain.csproj tests/Scheduler.UnitTests/Scheduler.UnitTests.csproj
```

- [ ] 1.4 Write the failing TimeRange tests

`tests/Scheduler.UnitTests/Scheduling/TimeRangeTests.cs`:

```csharp
using Scheduler.Domain.Scheduling;

namespace Scheduler.UnitTests.Scheduling;

public class TimeRangeTests
{
    private static readonly DateTimeOffset Nine = new(2030, 1, 7, 9, 0, 0, TimeSpan.Zero);

    private static TimeRange Minutes(int fromMinute, int toMinute) =>
        new(Nine.AddMinutes(fromMinute), Nine.AddMinutes(toMinute));

    [Theory]
    [InlineData("partial overlap", 0, 60, 30, 90, true)]
    [InlineData("containment", 0, 60, 15, 45, true)]
    [InlineData("identical ranges", 0, 60, 0, 60, true)]
    [InlineData("second starts when first ends", 0, 60, 60, 120, false)]
    [InlineData("second ends when first starts", 60, 120, 0, 60, false)]
    [InlineData("disjoint ranges", 0, 60, 90, 120, false)]
    public void Overlaps_uses_half_open_ranges(string scenario, int aFrom, int aTo, int bFrom, int bTo, bool expected)
    {
        var a = Minutes(aFrom, aTo);
        var b = Minutes(bFrom, bTo);

        Assert.True(a.Overlaps(b) == expected, scenario);
        Assert.True(b.Overlaps(a) == expected, scenario);
    }

    [Fact]
    public void FromDuration_computes_the_end_from_the_start()
    {
        var range = TimeRange.FromDuration(Nine, TimeSpan.FromMinutes(90));

        Assert.Equal(Nine, range.Start);
        Assert.Equal(Nine.AddMinutes(90), range.End);
    }

    [Fact]
    public void Rejects_an_end_that_is_not_after_the_start() =>
        Assert.Throws<ArgumentException>(() => new TimeRange(Nine, Nine));

    [Fact]
    public void Rejects_bounds_that_are_not_utc()
    {
        var local = new DateTimeOffset(2030, 1, 7, 9, 0, 0, TimeSpan.FromHours(7));

        Assert.Throws<ArgumentException>(() => new TimeRange(local, local.AddHours(1)));
    }
}
```

- [ ] 1.5 Run the tests and confirm they fail

Run: `dotnet test`
Expected: build fails with `CS0246: The type or namespace name 'TimeRange' could not be found`.

- [ ] 1.6 Implement TimeRange

`src/Scheduler.Domain/Scheduling/TimeRange.cs`:

```csharp
namespace Scheduler.Domain.Scheduling;

public readonly record struct TimeRange
{
    public TimeRange(DateTimeOffset start, DateTimeOffset end)
    {
        if (start.Offset != TimeSpan.Zero || end.Offset != TimeSpan.Zero)
            throw new ArgumentException("Time range bounds must be in UTC.");
        if (end <= start)
            throw new ArgumentException("Time range end must be after its start.");

        Start = start;
        End = end;
    }

    public DateTimeOffset Start { get; }

    public DateTimeOffset End { get; }

    public static TimeRange FromDuration(DateTimeOffset start, TimeSpan duration) => new(start, start + duration);

    public bool Overlaps(TimeRange other) => Start < other.End && other.Start < End;
}
```

- [ ] 1.7 Run the tests and confirm they pass

Run: `dotnet test`
Expected: `Test run summary: Passed!`, total 9, failed 0.

- [ ] 1.8 Add the CI workflow

`.github/workflows/ci.yml`:

```yaml
name: CI

on:
  push:
  pull_request:

jobs:
  build-and-test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          global-json-file: global.json
      - run: dotnet restore
      - run: dotnet build --no-restore --configuration Release
      - run: dotnet test --no-build --configuration Release
```

Run locally the same sequence to make sure it works: `dotnet restore && dotnet build --no-restore --configuration Release && dotnet test --no-build --configuration Release`. Expected: build succeeds, 9 tests pass.

- [ ] 1.9 Commit

```bash
git add global.json Directory.Build.props Directory.Packages.props VehicleServiceScheduler.slnx src tests .github PROJECT_STATE.md openspec/changes/add-appointment-booking/tasks.md
git commit -m "feat(domain): add solution skeleton, TimeRange and CI" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

## 2. Dealership opening hours and booking policy

**Files:**
- Create: `src/Scheduler.Domain/Dealerships/OpeningHours.cs`, `src/Scheduler.Domain/Dealerships/Dealership.cs`
- Create: `src/Scheduler.Domain/Customers/Customer.cs`, `src/Scheduler.Domain/Customers/Vehicle.cs`
- Create: `src/Scheduler.Domain/Scheduling/BookingRuleViolation.cs`, `src/Scheduler.Domain/Scheduling/BookingPolicy.cs`
- Create: `tests/Scheduler.UnitTests/TestData.cs`, `tests/Scheduler.UnitTests/Dealerships/DealershipOpeningHoursTests.cs`, `tests/Scheduler.UnitTests/Scheduling/BookingPolicyTests.cs`

**Interfaces:**
- Consumes: `TimeRange` (task 1).
- Produces:
  - `OpeningHours(DayOfWeek Day, TimeOnly Opens, TimeOnly Closes)` (record).
  - `Dealership(Guid id, string name, string timeZoneId, IEnumerable<OpeningHours> openingHours)` with `Id`, `Name`, `TimeZoneId`, `IReadOnlyList<OpeningHours> OpeningHours`, `DateTimeOffset ToLocalTime(DateTimeOffset instant)`, `bool IsOpenThroughout(TimeRange period)`.
  - `Customer(Guid id, string name, string email)`; `Vehicle(Guid id, Guid customerId, string vin, string make, string model, int year)`.
  - `enum BookingRuleViolation { VehicleNotOwnedByCustomer, StartInPast, StartNotOnSlotBoundary, OutsideOpeningHours, ServiceNotOffered }`.
  - `static class BookingPolicy` with `TimeSpan SlotLength` (15 minutes) and `BookingRuleViolation? Check(Dealership dealership, Vehicle vehicle, Guid customerId, TimeRange period, DateTimeOffset now)`.
  - Test helper `TestData` with `LondonId`, `SaigonId`, `CustomerId`, `Now`, `Utc(string)`, `London()`, `Saigon()`, `NewVehicle(Guid? customerId = null)`.

- [ ] 2.1 Write the test data helper

`tests/Scheduler.UnitTests/TestData.cs`:

```csharp
using System.Globalization;
using Scheduler.Domain.Customers;
using Scheduler.Domain.Dealerships;

namespace Scheduler.UnitTests;

internal static class TestData
{
    public static readonly Guid LondonId = Guid.Parse("d0000000-0000-4000-8000-000000000001");
    public static readonly Guid SaigonId = Guid.Parse("d0000000-0000-4000-8000-000000000002");
    public static readonly Guid CustomerId = Guid.Parse("c0000000-0000-4000-8000-000000000001");
    public static readonly DateTimeOffset Now = Utc("2030-01-01T00:00:00Z");

    public static DateTimeOffset Utc(string iso8601) =>
        DateTimeOffset.Parse(iso8601, CultureInfo.InvariantCulture).ToUniversalTime();

    public static Dealership London() => new(
        LondonId,
        "Riverside Motors London",
        "Europe/London",
        [.. Weekdays(new TimeOnly(8, 0), new TimeOnly(18, 0)), new OpeningHours(DayOfWeek.Saturday, new TimeOnly(9, 0), new TimeOnly(13, 0))]);

    public static Dealership Saigon() => new(
        SaigonId,
        "Saigon Auto Service",
        "Asia/Ho_Chi_Minh",
        [.. Weekdays(new TimeOnly(8, 0), new TimeOnly(17, 0))]);

    public static Vehicle NewVehicle(Guid? customerId = null) =>
        new(Guid.NewGuid(), customerId ?? CustomerId, "WVWZZZ1KZ6W000001", "Volkswagen", "Golf", 2021);

    private static IEnumerable<OpeningHours> Weekdays(TimeOnly opens, TimeOnly closes) =>
        new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday }
            .Select(day => new OpeningHours(day, opens, closes));
}
```

- [ ] 2.2 Write the failing opening-hours tests

`tests/Scheduler.UnitTests/Dealerships/DealershipOpeningHoursTests.cs`:

```csharp
using Scheduler.Domain.Scheduling;

namespace Scheduler.UnitTests.Dealerships;

public class DealershipOpeningHoursTests
{
    private static TimeRange OneHourFrom(string startUtc) =>
        TimeRange.FromDuration(TestData.Utc(startUtc), TimeSpan.FromHours(1));

    [Theory]
    [InlineData("winter Monday at opening time", "2030-01-07T08:00:00Z", true)]
    [InlineData("winter Monday ending at closing time", "2030-01-07T17:00:00Z", true)]
    [InlineData("winter Monday running past closing", "2030-01-07T17:30:00Z", false)]
    [InlineData("winter Monday before opening", "2030-01-07T07:45:00Z", false)]
    [InlineData("summer Monday at 08:00 BST", "2030-07-01T07:00:00Z", true)]
    [InlineData("summer Monday at 07:45 BST", "2030-07-01T06:45:00Z", false)]
    [InlineData("summer Monday ending at 18:00 BST", "2030-07-01T16:00:00Z", true)]
    [InlineData("summer Monday at 18:00 BST", "2030-07-01T17:00:00Z", false)]
    [InlineData("Saturday morning", "2030-01-05T12:00:00Z", true)]
    [InlineData("Saturday running past 13:00", "2030-01-05T12:30:00Z", false)]
    [InlineData("Sunday", "2030-01-06T10:00:00Z", false)]
    public void London_opening_hours_are_applied_in_local_time(string scenario, string startUtc, bool expected) =>
        Assert.True(TestData.London().IsOpenThroughout(OneHourFrom(startUtc)) == expected, scenario);

    [Theory]
    [InlineData("08:00 local", "2030-01-07T01:00:00Z", true)]
    [InlineData("07:45 local", "2030-01-07T00:45:00Z", false)]
    [InlineData("16:00 local ending at closing", "2030-01-07T09:00:00Z", true)]
    [InlineData("16:30 local running past closing", "2030-01-07T09:30:00Z", false)]
    public void Saigon_opening_hours_are_applied_in_local_time(string scenario, string startUtc, bool expected) =>
        Assert.True(TestData.Saigon().IsOpenThroughout(OneHourFrom(startUtc)) == expected, scenario);
}
```

- [ ] 2.3 Write the failing booking-policy tests

`tests/Scheduler.UnitTests/Scheduling/BookingPolicyTests.cs`:

```csharp
using Scheduler.Domain.Customers;
using Scheduler.Domain.Dealerships;
using Scheduler.Domain.Scheduling;

namespace Scheduler.UnitTests.Scheduling;

public class BookingPolicyTests
{
    private readonly Dealership _london = TestData.London();
    private readonly Vehicle _vehicle = TestData.NewVehicle();

    private BookingRuleViolation? Check(string startUtc, Guid? customerId = null, DateTimeOffset? now = null) =>
        BookingPolicy.Check(
            _london,
            _vehicle,
            customerId ?? TestData.CustomerId,
            TimeRange.FromDuration(TestData.Utc(startUtc), TimeSpan.FromHours(1)),
            now ?? TestData.Now);

    [Fact]
    public void Accepts_a_valid_booking() =>
        Assert.Null(Check("2030-01-07T09:00:00Z"));

    [Fact]
    public void Rejects_a_vehicle_registered_to_another_customer() =>
        Assert.Equal(BookingRuleViolation.VehicleNotOwnedByCustomer, Check("2030-01-07T09:00:00Z", customerId: Guid.NewGuid()));

    [Fact]
    public void Rejects_a_start_time_that_is_not_after_now() =>
        Assert.Equal(BookingRuleViolation.StartInPast, Check("2030-01-07T09:00:00Z", now: TestData.Utc("2030-01-07T09:00:00Z")));

    [Theory]
    [InlineData("2030-01-07T09:10:00Z")]
    [InlineData("2030-01-07T09:00:30Z")]
    public void Rejects_a_start_time_off_the_15_minute_grid(string startUtc) =>
        Assert.Equal(BookingRuleViolation.StartNotOnSlotBoundary, Check(startUtc));

    [Fact]
    public void Rejects_a_booking_outside_opening_hours() =>
        Assert.Equal(BookingRuleViolation.OutsideOpeningHours, Check("2030-01-06T10:00:00Z"));
}
```

- [ ] 2.4 Run the tests and confirm they fail

Run: `dotnet test`
Expected: build fails with `CS0234`/`CS0246` errors for `Scheduler.Domain.Dealerships`, `Scheduler.Domain.Customers` and `BookingPolicy`.

- [ ] 2.5 Implement the dealership and customer types

`src/Scheduler.Domain/Dealerships/OpeningHours.cs`:

```csharp
namespace Scheduler.Domain.Dealerships;

public sealed record OpeningHours(DayOfWeek Day, TimeOnly Opens, TimeOnly Closes);
```

`src/Scheduler.Domain/Dealerships/Dealership.cs`:

```csharp
using Scheduler.Domain.Scheduling;

namespace Scheduler.Domain.Dealerships;

public sealed class Dealership
{
    private readonly List<OpeningHours> _openingHours = [];

    private Dealership()
    {
    }

    public Dealership(Guid id, string name, string timeZoneId, IEnumerable<OpeningHours> openingHours)
    {
        Id = id;
        Name = name;
        TimeZoneId = timeZoneId;
        _openingHours.AddRange(openingHours);
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string TimeZoneId { get; private set; } = string.Empty;

    public IReadOnlyList<OpeningHours> OpeningHours => _openingHours;

    public DateTimeOffset ToLocalTime(DateTimeOffset instant) =>
        TimeZoneInfo.ConvertTime(instant, TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId));

    public bool IsOpenThroughout(TimeRange period)
    {
        var localStart = ToLocalTime(period.Start);
        var localEnd = ToLocalTime(period.End);
        if (localStart.Date != localEnd.Date)
            return false;

        var hours = _openingHours.FirstOrDefault(h => h.Day == localStart.DayOfWeek);
        return hours is not null
            && TimeOnly.FromTimeSpan(localStart.TimeOfDay) >= hours.Opens
            && TimeOnly.FromTimeSpan(localEnd.TimeOfDay) <= hours.Closes;
    }
}
```

`src/Scheduler.Domain/Customers/Customer.cs`:

```csharp
namespace Scheduler.Domain.Customers;

public sealed class Customer(Guid id, string name, string email)
{
    public Guid Id { get; private set; } = id;

    public string Name { get; private set; } = name;

    public string Email { get; private set; } = email;
}
```

`src/Scheduler.Domain/Customers/Vehicle.cs`:

```csharp
namespace Scheduler.Domain.Customers;

public sealed class Vehicle(Guid id, Guid customerId, string vin, string make, string model, int year)
{
    public Guid Id { get; private set; } = id;

    public Guid CustomerId { get; private set; } = customerId;

    public string Vin { get; private set; } = vin;

    public string Make { get; private set; } = make;

    public string Model { get; private set; } = model;

    public int Year { get; private set; } = year;
}
```

- [ ] 2.6 Implement the booking policy

`src/Scheduler.Domain/Scheduling/BookingRuleViolation.cs`:

```csharp
namespace Scheduler.Domain.Scheduling;

public enum BookingRuleViolation
{
    VehicleNotOwnedByCustomer,
    StartInPast,
    StartNotOnSlotBoundary,
    OutsideOpeningHours,
    ServiceNotOffered,
}
```

`src/Scheduler.Domain/Scheduling/BookingPolicy.cs`:

```csharp
using Scheduler.Domain.Customers;
using Scheduler.Domain.Dealerships;

namespace Scheduler.Domain.Scheduling;

public static class BookingPolicy
{
    public static readonly TimeSpan SlotLength = TimeSpan.FromMinutes(15);

    public static BookingRuleViolation? Check(Dealership dealership, Vehicle vehicle, Guid customerId, TimeRange period, DateTimeOffset now)
    {
        if (vehicle.CustomerId != customerId)
            return BookingRuleViolation.VehicleNotOwnedByCustomer;
        if (period.Start <= now)
            return BookingRuleViolation.StartInPast;
        if (dealership.ToLocalTime(period.Start).TimeOfDay.Ticks % SlotLength.Ticks != 0)
            return BookingRuleViolation.StartNotOnSlotBoundary;
        if (!dealership.IsOpenThroughout(period))
            return BookingRuleViolation.OutsideOpeningHours;

        return null;
    }
}
```

- [ ] 2.7 Run the tests and confirm they pass

Run: `dotnet test`
Expected: `Passed!`, total 30 (9 from task 1, 21 new), failed 0.

- [ ] 2.8 Commit

```bash
git add src tests openspec/changes/add-appointment-booking/tasks.md
git commit -m "feat(domain): add opening hours in local time and booking policy" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

## 3. Resources, appointments and the resource allocator

**Files:**
- Create: `src/Scheduler.Domain/Resources/BayType.cs`, `ServiceBay.cs`, `Qualification.cs`, `Technician.cs`
- Create: `src/Scheduler.Domain/Catalog/ServiceType.cs`
- Create: `src/Scheduler.Domain/Appointments/AppointmentStatus.cs`, `Appointment.cs`
- Create: `src/Scheduler.Domain/Scheduling/CapacityShortage.cs`, `AllocationResult.cs`, `ResourceAllocator.cs`
- Modify: `tests/Scheduler.UnitTests/TestData.cs`
- Create: `tests/Scheduler.UnitTests/Scheduling/ResourceAllocatorTests.cs`

**Interfaces:**
- Consumes: `TimeRange` (task 1); `TestData` (task 2).
- Produces:
  - `enum BayType { General, Alignment, ElectricVehicle }`.
  - `ServiceType(Guid id, string name, int durationMinutes, BayType requiredBayType)` with `Duration` (`TimeSpan`).
  - `ServiceBay(Guid id, Guid dealershipId, string name, BayType bayType)` with `bool Supports(ServiceType serviceType)`.
  - `Qualification(Guid ServiceTypeId)` (record); `Technician(Guid id, Guid dealershipId, string name, IEnumerable<Guid> qualifiedServiceTypeIds)` with `IReadOnlyList<Qualification> Qualifications` and `bool IsQualifiedFor(ServiceType serviceType)`.
  - `enum AppointmentStatus { Confirmed, Cancelled }`; `Appointment` with `Id`, `DealershipId`, `CustomerId`, `VehicleId`, `ServiceTypeId`, `ServiceBayId`, `TechnicianId`, `StartAt`, `EndAt`, `Status`, `CreatedAt`, `TimeRange Period`, and `static Appointment Confirm(Guid id, Guid dealershipId, Guid customerId, Guid vehicleId, Guid serviceTypeId, Guid serviceBayId, Guid technicianId, TimeRange period, DateTimeOffset createdAt)`.
  - `enum CapacityShortage { Bays, Technicians, BaysAndTechnicians }`.
  - `abstract record AllocationResult` with nested `Allocated(ServiceBay Bay, Technician Technician)`, `ServiceNotOffered`, `NoCapacity(CapacityShortage Shortage)`.
  - `static AllocationResult ResourceAllocator.Allocate(ServiceType serviceType, TimeRange period, IEnumerable<ServiceBay> bays, IEnumerable<Technician> technicians, IReadOnlyCollection<Appointment> confirmedAppointments)`.
  - `TestData.OilChange`, `TestData.WheelAlignment`, `TestData.NewBay(...)`, `TestData.NewTechnician(...)`, `TestData.Booked(...)`.

- [ ] 3.1 Extend the test data helper

In `tests/Scheduler.UnitTests/TestData.cs`, add these `using` directives:

```csharp
using Scheduler.Domain.Appointments;
using Scheduler.Domain.Catalog;
using Scheduler.Domain.Resources;
using Scheduler.Domain.Scheduling;
```

and add these members to the `TestData` class, after `NewVehicle`:

```csharp
    public static ServiceType OilChange { get; } =
        new(Guid.Parse("5e000000-0000-4000-8000-000000000001"), "Oil change", 60, BayType.General);

    public static ServiceType WheelAlignment { get; } =
        new(Guid.Parse("5e000000-0000-4000-8000-000000000003"), "Wheel alignment", 60, BayType.Alignment);

    public static ServiceBay NewBay(string name, BayType bayType = BayType.General, Guid? dealershipId = null) =>
        new(Guid.NewGuid(), dealershipId ?? LondonId, name, bayType);

    public static Technician NewTechnician(string name, params ServiceType[] qualifiedFor) =>
        new(Guid.NewGuid(), LondonId, name, qualifiedFor.Select(serviceType => serviceType.Id));

    public static Appointment Booked(ServiceBay bay, Technician technician, string startUtc, int minutes = 60, Guid? vehicleId = null) =>
        Appointment.Confirm(
            Guid.NewGuid(),
            bay.DealershipId,
            CustomerId,
            vehicleId ?? Guid.NewGuid(),
            OilChange.Id,
            bay.Id,
            technician.Id,
            TimeRange.FromDuration(Utc(startUtc), TimeSpan.FromMinutes(minutes)),
            Now);
```

- [ ] 3.2 Write the failing allocator tests

`tests/Scheduler.UnitTests/Scheduling/ResourceAllocatorTests.cs`:

```csharp
using Scheduler.Domain.Resources;
using Scheduler.Domain.Scheduling;

namespace Scheduler.UnitTests.Scheduling;

public class ResourceAllocatorTests
{
    private static readonly TimeRange NineToTen =
        TimeRange.FromDuration(TestData.Utc("2030-01-07T09:00:00Z"), TimeSpan.FromHours(1));

    [Fact]
    public void Allocates_the_first_free_bay_and_technician_ordered_by_name()
    {
        var bayB = TestData.NewBay("Bay B");
        var bayA = TestData.NewBay("Bay A");
        var ben = TestData.NewTechnician("Ben", TestData.OilChange);
        var alice = TestData.NewTechnician("Alice", TestData.OilChange);

        var result = ResourceAllocator.Allocate(TestData.OilChange, NineToTen, [bayB, bayA], [ben, alice], []);

        var allocated = Assert.IsType<AllocationResult.Allocated>(result);
        Assert.Same(bayA, allocated.Bay);
        Assert.Same(alice, allocated.Technician);
    }

    [Fact]
    public void Skips_a_bay_that_is_busy_during_the_period()
    {
        var bay1 = TestData.NewBay("Bay 1");
        var bay2 = TestData.NewBay("Bay 2");
        var alice = TestData.NewTechnician("Alice", TestData.OilChange);
        var ben = TestData.NewTechnician("Ben", TestData.OilChange);
        var bay1Busy = TestData.Booked(bay1, ben, "2030-01-07T08:30:00Z");

        var result = ResourceAllocator.Allocate(TestData.OilChange, NineToTen, [bay1, bay2], [alice, ben], [bay1Busy]);

        var allocated = Assert.IsType<AllocationResult.Allocated>(result);
        Assert.Same(bay2, allocated.Bay);
        Assert.Same(alice, allocated.Technician);
    }

    [Fact]
    public void Skips_a_technician_who_is_free_at_the_start_but_busy_later()
    {
        var bay = TestData.NewBay("Bay 1");
        var elsewhere = TestData.NewBay("Bay 9");
        var t2 = TestData.NewTechnician("T2", TestData.OilChange);
        var t3 = TestData.NewTechnician("T3", TestData.OilChange);
        var t2Later = TestData.Booked(elsewhere, t2, "2030-01-07T09:30:00Z");

        var result = ResourceAllocator.Allocate(TestData.OilChange, NineToTen, [bay], [t2, t3], [t2Later]);

        Assert.Same(t3, Assert.IsType<AllocationResult.Allocated>(result).Technician);
    }

    [Fact]
    public void Allows_back_to_back_appointments()
    {
        var bay = TestData.NewBay("Bay 1");
        var alice = TestData.NewTechnician("Alice", TestData.OilChange);
        var endsAtNine = TestData.Booked(bay, alice, "2030-01-07T08:00:00Z");

        var result = ResourceAllocator.Allocate(TestData.OilChange, NineToTen, [bay], [alice], [endsAtNine]);

        var allocated = Assert.IsType<AllocationResult.Allocated>(result);
        Assert.Same(bay, allocated.Bay);
        Assert.Same(alice, allocated.Technician);
    }

    [Fact]
    public void Never_allocates_an_incompatible_bay_or_an_unqualified_technician()
    {
        var evBay = TestData.NewBay("A EV bay", BayType.ElectricVehicle);
        var generalBay = TestData.NewBay("Z general bay");
        var aaron = TestData.NewTechnician("Aaron", TestData.WheelAlignment);
        var zoe = TestData.NewTechnician("Zoe", TestData.OilChange);

        var result = ResourceAllocator.Allocate(TestData.OilChange, NineToTen, [evBay, generalBay], [aaron, zoe], []);

        var allocated = Assert.IsType<AllocationResult.Allocated>(result);
        Assert.Same(generalBay, allocated.Bay);
        Assert.Same(zoe, allocated.Technician);
    }

    [Fact]
    public void Reports_service_not_offered_when_no_bay_is_compatible()
    {
        var result = ResourceAllocator.Allocate(
            TestData.OilChange,
            NineToTen,
            [TestData.NewBay("EV bay", BayType.ElectricVehicle)],
            [TestData.NewTechnician("Alice", TestData.OilChange)],
            []);

        Assert.IsType<AllocationResult.ServiceNotOffered>(result);
    }

    [Fact]
    public void Reports_service_not_offered_when_no_technician_is_qualified()
    {
        var result = ResourceAllocator.Allocate(
            TestData.OilChange,
            NineToTen,
            [TestData.NewBay("Bay 1")],
            [TestData.NewTechnician("Aaron", TestData.WheelAlignment)],
            []);

        Assert.IsType<AllocationResult.ServiceNotOffered>(result);
    }

    [Fact]
    public void Reports_a_bay_shortage_when_every_compatible_bay_is_busy()
    {
        var bay = TestData.NewBay("Bay 1");
        var alice = TestData.NewTechnician("Alice", TestData.OilChange);
        var ben = TestData.NewTechnician("Ben", TestData.OilChange);
        var busy = TestData.Booked(bay, ben, "2030-01-07T09:00:00Z");

        var result = ResourceAllocator.Allocate(TestData.OilChange, NineToTen, [bay], [alice, ben], [busy]);

        Assert.Equal(CapacityShortage.Bays, Assert.IsType<AllocationResult.NoCapacity>(result).Shortage);
    }

    [Fact]
    public void Reports_a_technician_shortage_when_every_qualified_technician_is_busy()
    {
        var bay1 = TestData.NewBay("Bay 1");
        var bay2 = TestData.NewBay("Bay 2");
        var alice = TestData.NewTechnician("Alice", TestData.OilChange);
        var busy = TestData.Booked(bay2, alice, "2030-01-07T09:00:00Z");

        var result = ResourceAllocator.Allocate(TestData.OilChange, NineToTen, [bay1, bay2], [alice], [busy]);

        Assert.Equal(CapacityShortage.Technicians, Assert.IsType<AllocationResult.NoCapacity>(result).Shortage);
    }

    [Fact]
    public void Reports_both_shortages_when_bays_and_technicians_are_busy()
    {
        var bay = TestData.NewBay("Bay 1");
        var alice = TestData.NewTechnician("Alice", TestData.OilChange);
        var busy = TestData.Booked(bay, alice, "2030-01-07T09:00:00Z");

        var result = ResourceAllocator.Allocate(TestData.OilChange, NineToTen, [bay], [alice], [busy]);

        Assert.Equal(CapacityShortage.BaysAndTechnicians, Assert.IsType<AllocationResult.NoCapacity>(result).Shortage);
    }
}
```

- [ ] 3.3 Run the tests and confirm they fail

Run: `dotnet test`
Expected: build fails with `CS0246`/`CS0234` errors for `ServiceType`, `ServiceBay`, `Technician`, `Appointment`, `ResourceAllocator`.

- [ ] 3.4 Implement the resource and catalog types

`src/Scheduler.Domain/Resources/BayType.cs`:

```csharp
namespace Scheduler.Domain.Resources;

public enum BayType
{
    General,
    Alignment,
    ElectricVehicle,
}
```

`src/Scheduler.Domain/Catalog/ServiceType.cs`:

```csharp
using Scheduler.Domain.Resources;

namespace Scheduler.Domain.Catalog;

public sealed class ServiceType(Guid id, string name, int durationMinutes, BayType requiredBayType)
{
    public Guid Id { get; private set; } = id;

    public string Name { get; private set; } = name;

    public int DurationMinutes { get; private set; } = durationMinutes;

    public BayType RequiredBayType { get; private set; } = requiredBayType;

    public TimeSpan Duration => TimeSpan.FromMinutes(DurationMinutes);
}
```

`src/Scheduler.Domain/Resources/ServiceBay.cs`:

```csharp
using Scheduler.Domain.Catalog;

namespace Scheduler.Domain.Resources;

public sealed class ServiceBay(Guid id, Guid dealershipId, string name, BayType bayType)
{
    public Guid Id { get; private set; } = id;

    public Guid DealershipId { get; private set; } = dealershipId;

    public string Name { get; private set; } = name;

    public BayType BayType { get; private set; } = bayType;

    public bool Supports(ServiceType serviceType) => BayType == serviceType.RequiredBayType;
}
```

`src/Scheduler.Domain/Resources/Qualification.cs`:

```csharp
namespace Scheduler.Domain.Resources;

public sealed record Qualification(Guid ServiceTypeId);
```

`src/Scheduler.Domain/Resources/Technician.cs`:

```csharp
using Scheduler.Domain.Catalog;

namespace Scheduler.Domain.Resources;

public sealed class Technician
{
    private readonly List<Qualification> _qualifications = [];

    private Technician()
    {
    }

    public Technician(Guid id, Guid dealershipId, string name, IEnumerable<Guid> qualifiedServiceTypeIds)
    {
        Id = id;
        DealershipId = dealershipId;
        Name = name;
        _qualifications.AddRange(qualifiedServiceTypeIds.Select(serviceTypeId => new Qualification(serviceTypeId)));
    }

    public Guid Id { get; private set; }

    public Guid DealershipId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public IReadOnlyList<Qualification> Qualifications => _qualifications;

    public bool IsQualifiedFor(ServiceType serviceType) =>
        _qualifications.Any(qualification => qualification.ServiceTypeId == serviceType.Id);
}
```

- [ ] 3.5 Implement the appointment

`src/Scheduler.Domain/Appointments/AppointmentStatus.cs`:

```csharp
namespace Scheduler.Domain.Appointments;

public enum AppointmentStatus
{
    Confirmed,
    Cancelled,
}
```

`src/Scheduler.Domain/Appointments/Appointment.cs`:

```csharp
using Scheduler.Domain.Scheduling;

namespace Scheduler.Domain.Appointments;

public sealed class Appointment
{
    private Appointment()
    {
    }

    public Guid Id { get; private set; }

    public Guid DealershipId { get; private set; }

    public Guid CustomerId { get; private set; }

    public Guid VehicleId { get; private set; }

    public Guid ServiceTypeId { get; private set; }

    public Guid ServiceBayId { get; private set; }

    public Guid TechnicianId { get; private set; }

    public DateTimeOffset StartAt { get; private set; }

    public DateTimeOffset EndAt { get; private set; }

    public AppointmentStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public TimeRange Period => new(StartAt, EndAt);

    public static Appointment Confirm(
        Guid id,
        Guid dealershipId,
        Guid customerId,
        Guid vehicleId,
        Guid serviceTypeId,
        Guid serviceBayId,
        Guid technicianId,
        TimeRange period,
        DateTimeOffset createdAt) =>
        new()
        {
            Id = id,
            DealershipId = dealershipId,
            CustomerId = customerId,
            VehicleId = vehicleId,
            ServiceTypeId = serviceTypeId,
            ServiceBayId = serviceBayId,
            TechnicianId = technicianId,
            StartAt = period.Start,
            EndAt = period.End,
            Status = AppointmentStatus.Confirmed,
            CreatedAt = createdAt,
        };
}
```

- [ ] 3.6 Implement the allocator

`src/Scheduler.Domain/Scheduling/CapacityShortage.cs`:

```csharp
namespace Scheduler.Domain.Scheduling;

public enum CapacityShortage
{
    Bays,
    Technicians,
    BaysAndTechnicians,
}
```

`src/Scheduler.Domain/Scheduling/AllocationResult.cs`:

```csharp
using Scheduler.Domain.Resources;

namespace Scheduler.Domain.Scheduling;

public abstract record AllocationResult
{
    private AllocationResult()
    {
    }

    public sealed record Allocated(ServiceBay Bay, Technician Technician) : AllocationResult;

    public sealed record ServiceNotOffered : AllocationResult;

    public sealed record NoCapacity(CapacityShortage Shortage) : AllocationResult;
}
```

`src/Scheduler.Domain/Scheduling/ResourceAllocator.cs`:

```csharp
using Scheduler.Domain.Appointments;
using Scheduler.Domain.Catalog;
using Scheduler.Domain.Resources;

namespace Scheduler.Domain.Scheduling;

public static class ResourceAllocator
{
    public static AllocationResult Allocate(
        ServiceType serviceType,
        TimeRange period,
        IEnumerable<ServiceBay> bays,
        IEnumerable<Technician> technicians,
        IReadOnlyCollection<Appointment> confirmedAppointments)
    {
        var compatibleBays = bays
            .Where(bay => bay.Supports(serviceType))
            .OrderBy(bay => bay.Name, StringComparer.Ordinal)
            .ThenBy(bay => bay.Id)
            .ToList();
        var qualifiedTechnicians = technicians
            .Where(technician => technician.IsQualifiedFor(serviceType))
            .OrderBy(technician => technician.Name, StringComparer.Ordinal)
            .ThenBy(technician => technician.Id)
            .ToList();
        if (compatibleBays.Count == 0 || qualifiedTechnicians.Count == 0)
            return new AllocationResult.ServiceNotOffered();

        var overlapping = confirmedAppointments.Where(appointment => appointment.Period.Overlaps(period)).ToList();
        var freeBay = compatibleBays.FirstOrDefault(bay => overlapping.All(a => a.ServiceBayId != bay.Id));
        var freeTechnician = qualifiedTechnicians.FirstOrDefault(technician => overlapping.All(a => a.TechnicianId != technician.Id));
        if (freeBay is not null && freeTechnician is not null)
            return new AllocationResult.Allocated(freeBay, freeTechnician);

        var shortage = (freeBay, freeTechnician) switch
        {
            (null, null) => CapacityShortage.BaysAndTechnicians,
            (null, _) => CapacityShortage.Bays,
            _ => CapacityShortage.Technicians,
        };
        return new AllocationResult.NoCapacity(shortage);
    }
}
```

- [ ] 3.7 Run the tests and confirm they pass

Run: `dotnet test`
Expected: `Passed!`, total 40 (10 new), failed 0.

- [ ] 3.8 Commit

```bash
git add src tests openspec/changes/add-appointment-booking/tasks.md
git commit -m "feat(domain): add bays, technicians, appointments and resource allocator" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

## 4. Booking use case

**Files:**
- Create: `src/Scheduler.Application/Scheduler.Application.csproj`
- Create: `src/Scheduler.Application/Appointments/AppointmentDetails.cs`, `IAppointmentQueries.cs`
- Create: `src/Scheduler.Application/Booking/IBookingStore.cs`, `BookAppointmentCommand.cs`, `BookAppointmentResult.cs`, `BookAppointmentHandler.cs`
- Modify: `tests/Scheduler.UnitTests/Scheduler.UnitTests.csproj`, `VehicleServiceScheduler.slnx`
- Create: `tests/Scheduler.UnitTests/Booking/FakeBookingStore.cs`, `BookingScenario.cs`, `BookAppointmentHandlerTests.cs`

**Interfaces:**
- Consumes: all domain types from tasks 1–3.
- Produces:
  - `record AppointmentDetails(Guid Id, string Status, Guid CustomerId, Guid VehicleId, DealershipSummary Dealership, ServiceTypeSummary ServiceType, ResourceSummary ServiceBay, ResourceSummary Technician, DateTimeOffset StartAt, DateTimeOffset EndAt, DateTimeOffset CreatedAt)` with `static AppointmentDetails From(Appointment, Dealership, ServiceType, ServiceBay, Technician)`; `record DealershipSummary(Guid Id, string Name, string TimeZone)`; `record ServiceTypeSummary(Guid Id, string Name, int DurationMinutes)`; `record ResourceSummary(Guid Id, string Name)`.
  - `interface IAppointmentQueries { Task<AppointmentDetails?> FindByIdAsync(Guid id, CancellationToken cancellationToken); }`
  - `interface IBookingStore` with `LoadReferencesAsync(BookAppointmentCommand, CancellationToken) : Task<BookingReferences>`, `LoadScheduleAsync(Guid dealershipId, Guid vehicleId, TimeRange period, CancellationToken) : Task<ResourceSchedule>`, `TryAddAsync(Appointment, CancellationToken) : Task<AddAppointmentResult>`; `record BookingReferences(Dealership? Dealership, ServiceType? ServiceType, Vehicle? Vehicle, bool CustomerExists)`; `record ResourceSchedule(IReadOnlyList<ServiceBay> Bays, IReadOnlyList<Technician> Technicians, IReadOnlyList<Appointment> ConfirmedDealershipAppointments, IReadOnlyList<Appointment> ConfirmedVehicleAppointments)`; `enum AddAppointmentResult { Added, BayTaken, TechnicianTaken, VehicleTaken }`.
  - `record BookAppointmentCommand(Guid CustomerId, Guid VehicleId, Guid DealershipId, Guid ServiceTypeId, DateTimeOffset StartAt)`.
  - `abstract record BookAppointmentResult` with nested `Booked(AppointmentDetails Appointment)`, `NotFound(string Resource, Guid Id)`, `RuleViolated(BookingRuleViolation Violation)`, `NoCapacity(CapacityShortage Shortage)`, `VehicleAlreadyBooked`.
  - `sealed class BookAppointmentHandler(IBookingStore store, TimeProvider clock)` with `const int MaxAttempts = 3` and `Task<BookAppointmentResult> HandleAsync(BookAppointmentCommand command, CancellationToken cancellationToken)`.

- [ ] 4.1 Create the Application project and reference it from the unit tests

`src/Scheduler.Application/Scheduler.Application.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\Scheduler.Domain\Scheduler.Domain.csproj" />
  </ItemGroup>
</Project>
```

In `tests/Scheduler.UnitTests/Scheduler.UnitTests.csproj`, add to the `ProjectReference` item group:

```xml
    <ProjectReference Include="..\..\src\Scheduler.Application\Scheduler.Application.csproj" />
```

```bash
dotnet sln VehicleServiceScheduler.slnx add src/Scheduler.Application/Scheduler.Application.csproj
```

- [ ] 4.2 Write the test doubles

`tests/Scheduler.UnitTests/Booking/FakeBookingStore.cs`:

```csharp
using Scheduler.Application.Booking;
using Scheduler.Domain.Appointments;
using Scheduler.Domain.Catalog;
using Scheduler.Domain.Customers;
using Scheduler.Domain.Dealerships;
using Scheduler.Domain.Resources;
using Scheduler.Domain.Scheduling;

namespace Scheduler.UnitTests.Booking;

internal sealed class FakeBookingStore : IBookingStore
{
    public List<Dealership> Dealerships { get; } = [];

    public List<ServiceType> ServiceTypes { get; } = [];

    public List<Vehicle> Vehicles { get; } = [];

    public List<Guid> CustomerIds { get; } = [];

    public List<ServiceBay> Bays { get; } = [];

    public List<Technician> Technicians { get; } = [];

    public List<Appointment> Appointments { get; } = [];

    public Queue<(AddAppointmentResult Result, Appointment? Winner)> Conflicts { get; } = new();

    public int ScheduleLoads { get; private set; }

    public Task<BookingReferences> LoadReferencesAsync(BookAppointmentCommand command, CancellationToken cancellationToken) =>
        Task.FromResult(new BookingReferences(
            Dealerships.SingleOrDefault(d => d.Id == command.DealershipId),
            ServiceTypes.SingleOrDefault(s => s.Id == command.ServiceTypeId),
            Vehicles.SingleOrDefault(v => v.Id == command.VehicleId),
            CustomerIds.Contains(command.CustomerId)));

    public Task<ResourceSchedule> LoadScheduleAsync(Guid dealershipId, Guid vehicleId, TimeRange period, CancellationToken cancellationToken)
    {
        ScheduleLoads++;
        var overlapping = Appointments.Where(a => a.Period.Overlaps(period)).ToList();
        return Task.FromResult(new ResourceSchedule(
            Bays.Where(b => b.DealershipId == dealershipId).ToList(),
            Technicians.Where(t => t.DealershipId == dealershipId).ToList(),
            overlapping.Where(a => a.DealershipId == dealershipId).ToList(),
            overlapping.Where(a => a.VehicleId == vehicleId).ToList()));
    }

    public Task<AddAppointmentResult> TryAddAsync(Appointment appointment, CancellationToken cancellationToken)
    {
        if (Conflicts.TryDequeue(out var conflict))
        {
            if (conflict.Winner is not null)
                Appointments.Add(conflict.Winner);
            return Task.FromResult(conflict.Result);
        }

        Appointments.Add(appointment);
        return Task.FromResult(AddAppointmentResult.Added);
    }
}
```

`tests/Scheduler.UnitTests/Booking/BookingScenario.cs`:

```csharp
using System.Globalization;
using Microsoft.Extensions.Time.Testing;
using Scheduler.Application.Booking;
using Scheduler.Domain.Customers;
using Scheduler.Domain.Dealerships;
using Scheduler.Domain.Resources;

namespace Scheduler.UnitTests.Booking;

internal sealed class BookingScenario
{
    public BookingScenario()
    {
        Store.Dealerships.Add(London);
        Store.ServiceTypes.Add(TestData.OilChange);
        Store.Vehicles.Add(Vehicle);
        Store.CustomerIds.Add(TestData.CustomerId);
        Store.Bays.AddRange([Bay1, Bay2]);
        Store.Technicians.AddRange([Alice, Ben]);
    }

    public FakeBookingStore Store { get; } = new();

    public FakeTimeProvider Clock { get; } = new(TestData.Now);

    public Dealership London { get; } = TestData.London();

    public Vehicle Vehicle { get; } = TestData.NewVehicle();

    public ServiceBay Bay1 { get; } = TestData.NewBay("Bay 1");

    public ServiceBay Bay2 { get; } = TestData.NewBay("Bay 2");

    public Technician Alice { get; } = TestData.NewTechnician("Alice", TestData.OilChange);

    public Technician Ben { get; } = TestData.NewTechnician("Ben", TestData.OilChange);

    public BookAppointmentHandler Handler() => new(Store, Clock);

    public BookAppointmentCommand Command(string startAt = "2030-01-07T09:00:00Z") =>
        new(TestData.CustomerId, Vehicle.Id, London.Id, TestData.OilChange.Id, DateTimeOffset.Parse(startAt, CultureInfo.InvariantCulture));
}
```

- [ ] 4.3 Write the failing handler tests

`tests/Scheduler.UnitTests/Booking/BookAppointmentHandlerTests.cs`:

```csharp
using Scheduler.Application.Booking;
using Scheduler.Domain.Scheduling;

namespace Scheduler.UnitTests.Booking;

public class BookAppointmentHandlerTests
{
    private readonly BookingScenario _scenario = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Task<BookAppointmentResult> BookAsync(BookAppointmentCommand? command = null) =>
        _scenario.Handler().HandleAsync(command ?? _scenario.Command(), Ct);

    [Fact]
    public async Task Books_the_first_free_bay_and_technician()
    {
        var booked = Assert.IsType<BookAppointmentResult.Booked>(await BookAsync());

        Assert.Equal("Bay 1", booked.Appointment.ServiceBay.Name);
        Assert.Equal("Alice", booked.Appointment.Technician.Name);
        Assert.Equal("Confirmed", booked.Appointment.Status);
        Assert.Equal(TestData.Utc("2030-01-07T10:00:00Z"), booked.Appointment.EndAt);
        Assert.Equal("Europe/London", booked.Appointment.Dealership.TimeZone);
        Assert.Single(_scenario.Store.Appointments);
    }

    [Fact]
    public async Task Normalises_the_start_time_to_utc()
    {
        var booked = Assert.IsType<BookAppointmentResult.Booked>(await BookAsync(_scenario.Command("2030-01-07T10:00:00+01:00")));

        Assert.Equal(TestData.Utc("2030-01-07T09:00:00Z"), booked.Appointment.StartAt);
        Assert.Equal(TimeSpan.Zero, booked.Appointment.StartAt.Offset);
    }

    [Theory]
    [InlineData("dealership")]
    [InlineData("serviceType")]
    [InlineData("customer")]
    [InlineData("vehicle")]
    public async Task Returns_not_found_for_an_unknown_reference(string missing)
    {
        var command = _scenario.Command();
        command = missing switch
        {
            "dealership" => command with { DealershipId = Guid.NewGuid() },
            "serviceType" => command with { ServiceTypeId = Guid.NewGuid() },
            "customer" => command with { CustomerId = Guid.NewGuid() },
            _ => command with { VehicleId = Guid.NewGuid() },
        };

        var notFound = Assert.IsType<BookAppointmentResult.NotFound>(await BookAsync(command));

        Assert.Equal(missing, notFound.Resource);
    }

    [Fact]
    public async Task Returns_the_rule_violation_reported_by_the_booking_policy()
    {
        _scenario.Clock.SetUtcNow(TestData.Utc("2030-01-08T00:00:00Z"));

        var violated = Assert.IsType<BookAppointmentResult.RuleViolated>(await BookAsync());

        Assert.Equal(BookingRuleViolation.StartInPast, violated.Violation);
    }

    [Fact]
    public async Task Returns_service_not_offered_when_the_dealership_cannot_do_the_service()
    {
        _scenario.Store.ServiceTypes.Add(TestData.WheelAlignment);

        var result = await BookAsync(_scenario.Command() with { ServiceTypeId = TestData.WheelAlignment.Id });

        Assert.Equal(BookingRuleViolation.ServiceNotOffered, Assert.IsType<BookAppointmentResult.RuleViolated>(result).Violation);
    }

    [Fact]
    public async Task Returns_a_technician_shortage_when_every_qualified_technician_is_busy()
    {
        var elsewhere = TestData.NewBay("Bay 9");
        _scenario.Store.Appointments.Add(TestData.Booked(elsewhere, _scenario.Alice, "2030-01-07T09:00:00Z"));
        _scenario.Store.Appointments.Add(TestData.Booked(elsewhere, _scenario.Ben, "2030-01-07T09:00:00Z"));

        var noCapacity = Assert.IsType<BookAppointmentResult.NoCapacity>(await BookAsync());

        Assert.Equal(CapacityShortage.Technicians, noCapacity.Shortage);
    }

    [Fact]
    public async Task Returns_vehicle_already_booked_when_the_vehicle_has_an_overlapping_appointment_elsewhere()
    {
        var saigonBay = TestData.NewBay("Bay A", dealershipId: TestData.SaigonId);
        var minh = TestData.NewTechnician("Minh", TestData.OilChange);
        _scenario.Store.Appointments.Add(TestData.Booked(saigonBay, minh, "2030-01-07T09:30:00Z", vehicleId: _scenario.Vehicle.Id));

        Assert.IsType<BookAppointmentResult.VehicleAlreadyBooked>(await BookAsync());
    }

    [Fact]
    public async Task Retries_with_the_next_free_pair_after_losing_a_race()
    {
        var winner = TestData.Booked(_scenario.Bay1, _scenario.Alice, "2030-01-07T09:00:00Z");
        _scenario.Store.Conflicts.Enqueue((AddAppointmentResult.BayTaken, winner));

        var booked = Assert.IsType<BookAppointmentResult.Booked>(await BookAsync());

        Assert.Equal(_scenario.Bay2.Id, booked.Appointment.ServiceBay.Id);
        Assert.Equal(_scenario.Ben.Id, booked.Appointment.Technician.Id);
        Assert.Equal(2, _scenario.Store.ScheduleLoads);
    }

    [Fact]
    public async Task Gives_up_after_the_maximum_number_of_attempts()
    {
        for (var attempt = 0; attempt < BookAppointmentHandler.MaxAttempts; attempt++)
            _scenario.Store.Conflicts.Enqueue((AddAppointmentResult.TechnicianTaken, null));

        var noCapacity = Assert.IsType<BookAppointmentResult.NoCapacity>(await BookAsync());

        Assert.Equal(CapacityShortage.Technicians, noCapacity.Shortage);
        Assert.Equal(BookAppointmentHandler.MaxAttempts, _scenario.Store.ScheduleLoads);
    }

    [Fact]
    public async Task Does_not_retry_when_the_vehicle_was_booked_concurrently()
    {
        _scenario.Store.Conflicts.Enqueue((AddAppointmentResult.VehicleTaken, null));

        Assert.IsType<BookAppointmentResult.VehicleAlreadyBooked>(await BookAsync());
        Assert.Equal(1, _scenario.Store.ScheduleLoads);
    }
}
```

- [ ] 4.4 Run the tests and confirm they fail

Run: `dotnet test`
Expected: build fails with `CS0234`/`CS0246` errors for `Scheduler.Application.Booking` types.

- [ ] 4.5 Implement the read model and query port

`src/Scheduler.Application/Appointments/AppointmentDetails.cs`:

```csharp
using Scheduler.Domain.Appointments;
using Scheduler.Domain.Catalog;
using Scheduler.Domain.Dealerships;
using Scheduler.Domain.Resources;

namespace Scheduler.Application.Appointments;

public sealed record AppointmentDetails(
    Guid Id,
    string Status,
    Guid CustomerId,
    Guid VehicleId,
    DealershipSummary Dealership,
    ServiceTypeSummary ServiceType,
    ResourceSummary ServiceBay,
    ResourceSummary Technician,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    DateTimeOffset CreatedAt)
{
    public static AppointmentDetails From(
        Appointment appointment,
        Dealership dealership,
        ServiceType serviceType,
        ServiceBay bay,
        Technician technician) =>
        new(
            appointment.Id,
            appointment.Status.ToString(),
            appointment.CustomerId,
            appointment.VehicleId,
            new DealershipSummary(dealership.Id, dealership.Name, dealership.TimeZoneId),
            new ServiceTypeSummary(serviceType.Id, serviceType.Name, serviceType.DurationMinutes),
            new ResourceSummary(bay.Id, bay.Name),
            new ResourceSummary(technician.Id, technician.Name),
            appointment.StartAt,
            appointment.EndAt,
            appointment.CreatedAt);
}

public sealed record DealershipSummary(Guid Id, string Name, string TimeZone);

public sealed record ServiceTypeSummary(Guid Id, string Name, int DurationMinutes);

public sealed record ResourceSummary(Guid Id, string Name);
```

`src/Scheduler.Application/Appointments/IAppointmentQueries.cs`:

```csharp
namespace Scheduler.Application.Appointments;

public interface IAppointmentQueries
{
    Task<AppointmentDetails?> FindByIdAsync(Guid id, CancellationToken cancellationToken);
}
```

- [ ] 4.6 Implement the booking port, command and result

`src/Scheduler.Application/Booking/BookAppointmentCommand.cs`:

```csharp
namespace Scheduler.Application.Booking;

public sealed record BookAppointmentCommand(
    Guid CustomerId,
    Guid VehicleId,
    Guid DealershipId,
    Guid ServiceTypeId,
    DateTimeOffset StartAt);
```

`src/Scheduler.Application/Booking/IBookingStore.cs`:

```csharp
using Scheduler.Domain.Appointments;
using Scheduler.Domain.Catalog;
using Scheduler.Domain.Customers;
using Scheduler.Domain.Dealerships;
using Scheduler.Domain.Resources;
using Scheduler.Domain.Scheduling;

namespace Scheduler.Application.Booking;

public interface IBookingStore
{
    Task<BookingReferences> LoadReferencesAsync(BookAppointmentCommand command, CancellationToken cancellationToken);

    Task<ResourceSchedule> LoadScheduleAsync(Guid dealershipId, Guid vehicleId, TimeRange period, CancellationToken cancellationToken);

    Task<AddAppointmentResult> TryAddAsync(Appointment appointment, CancellationToken cancellationToken);
}

public sealed record BookingReferences(Dealership? Dealership, ServiceType? ServiceType, Vehicle? Vehicle, bool CustomerExists);

public sealed record ResourceSchedule(
    IReadOnlyList<ServiceBay> Bays,
    IReadOnlyList<Technician> Technicians,
    IReadOnlyList<Appointment> ConfirmedDealershipAppointments,
    IReadOnlyList<Appointment> ConfirmedVehicleAppointments);

public enum AddAppointmentResult
{
    Added,
    BayTaken,
    TechnicianTaken,
    VehicleTaken,
}
```

`src/Scheduler.Application/Booking/BookAppointmentResult.cs`:

```csharp
using Scheduler.Application.Appointments;
using Scheduler.Domain.Scheduling;

namespace Scheduler.Application.Booking;

public abstract record BookAppointmentResult
{
    private BookAppointmentResult()
    {
    }

    public sealed record Booked(AppointmentDetails Appointment) : BookAppointmentResult;

    public sealed record NotFound(string Resource, Guid Id) : BookAppointmentResult;

    public sealed record RuleViolated(BookingRuleViolation Violation) : BookAppointmentResult;

    public sealed record NoCapacity(CapacityShortage Shortage) : BookAppointmentResult;

    public sealed record VehicleAlreadyBooked : BookAppointmentResult;
}
```

- [ ] 4.7 Implement the handler

`src/Scheduler.Application/Booking/BookAppointmentHandler.cs`:

```csharp
using Scheduler.Application.Appointments;
using Scheduler.Domain.Appointments;
using Scheduler.Domain.Scheduling;

namespace Scheduler.Application.Booking;

public sealed class BookAppointmentHandler(IBookingStore store, TimeProvider clock)
{
    public const int MaxAttempts = 3;

    public async Task<BookAppointmentResult> HandleAsync(BookAppointmentCommand command, CancellationToken cancellationToken)
    {
        var references = await store.LoadReferencesAsync(command, cancellationToken);
        if (references.Dealership is not { } dealership)
            return new BookAppointmentResult.NotFound("dealership", command.DealershipId);
        if (references.ServiceType is not { } serviceType)
            return new BookAppointmentResult.NotFound("serviceType", command.ServiceTypeId);
        if (!references.CustomerExists)
            return new BookAppointmentResult.NotFound("customer", command.CustomerId);
        if (references.Vehicle is not { } vehicle)
            return new BookAppointmentResult.NotFound("vehicle", command.VehicleId);

        var now = clock.GetUtcNow();
        var period = TimeRange.FromDuration(command.StartAt.ToUniversalTime(), serviceType.Duration);
        if (BookingPolicy.Check(dealership, vehicle, command.CustomerId, period, now) is { } violation)
            return new BookAppointmentResult.RuleViolated(violation);

        for (var attempt = 1; ; attempt++)
        {
            var schedule = await store.LoadScheduleAsync(dealership.Id, vehicle.Id, period, cancellationToken);
            if (schedule.ConfirmedVehicleAppointments.Any(appointment => appointment.Period.Overlaps(period)))
                return new BookAppointmentResult.VehicleAlreadyBooked();

            var allocation = ResourceAllocator.Allocate(
                serviceType,
                period,
                schedule.Bays,
                schedule.Technicians,
                schedule.ConfirmedDealershipAppointments);
            if (allocation is AllocationResult.ServiceNotOffered)
                return new BookAppointmentResult.RuleViolated(BookingRuleViolation.ServiceNotOffered);
            if (allocation is AllocationResult.NoCapacity noCapacity)
                return new BookAppointmentResult.NoCapacity(noCapacity.Shortage);

            var (bay, technician) = (AllocationResult.Allocated)allocation;
            var appointment = Appointment.Confirm(
                Guid.CreateVersion7(now),
                dealership.Id,
                command.CustomerId,
                vehicle.Id,
                serviceType.Id,
                bay.Id,
                technician.Id,
                period,
                now);

            switch (await store.TryAddAsync(appointment, cancellationToken))
            {
                case AddAppointmentResult.Added:
                    return new BookAppointmentResult.Booked(AppointmentDetails.From(appointment, dealership, serviceType, bay, technician));
                case AddAppointmentResult.VehicleTaken:
                    return new BookAppointmentResult.VehicleAlreadyBooked();
                case AddAppointmentResult.BayTaken when attempt == MaxAttempts:
                    return new BookAppointmentResult.NoCapacity(CapacityShortage.Bays);
                case AddAppointmentResult.TechnicianTaken when attempt == MaxAttempts:
                    return new BookAppointmentResult.NoCapacity(CapacityShortage.Technicians);
            }
        }
    }
}
```

- [ ] 4.8 Run the tests and confirm they pass

Run: `dotnet test`
Expected: `Passed!`, total 53 (13 new), failed 0.

- [ ] 4.9 Commit

```bash
git add src tests VehicleServiceScheduler.slnx openspec/changes/add-appointment-booking/tasks.md
git commit -m "feat(application): add booking use case with bounded retry" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

## 5. Persistence with exclusion constraints and seed data

**Files:**
- Create: `src/Scheduler.Infrastructure/Scheduler.Infrastructure.csproj`
- Create: `src/Scheduler.Infrastructure/Persistence/SchedulerDbContext.cs`, `SchedulerDbContextFactory.cs`, `AppointmentConstraints.cs`, `BookingStore.cs`, `AppointmentQueries.cs`
- Create (generated, then edited): `src/Scheduler.Infrastructure/Persistence/Migrations/*_InitialSchema.cs`
- Create: `src/Scheduler.Infrastructure/Seeding/SeedData.cs`, `DatabaseSeeder.cs`
- Create: `src/Scheduler.Infrastructure/DependencyInjection.cs`
- Create: `dotnet-tools.json`
- Create: `tests/Scheduler.IntegrationTests/Scheduler.IntegrationTests.csproj`, `PostgresFixture.cs`, `Persistence/BookingStoreTests.cs`
- Modify: `VehicleServiceScheduler.slnx`

**Interfaces:**
- Consumes: `IBookingStore`, `BookingReferences`, `ResourceSchedule`, `AddAppointmentResult`, `IAppointmentQueries`, `AppointmentDetails` (task 4); domain types.
- Produces:
  - `SchedulerDbContext` with `DbSet`s `Dealerships`, `ServiceTypes`, `ServiceBays`, `Technicians`, `Customers`, `Vehicles`, `Appointments`.
  - `AppointmentConstraints.BayNoOverlap`, `TechnicianNoOverlap`, `VehicleNoOverlap` (constraint names).
  - `BookingStore(SchedulerDbContext db) : IBookingStore`; `AppointmentQueries(SchedulerDbContext db) : IAppointmentQueries`.
  - `SeedData.Dealerships.{London, Saigon}`, `SeedData.ServiceTypes.{OilChange, BrakeInspection, WheelAlignment, EvBatteryCheck}`, `SeedData.Bays.{LondonBay1, LondonBay2, LondonAlignmentBay, LondonEvBay, SaigonBayA, SaigonBayB}`, `SeedData.Technicians.{Alice, Ben, Chloe, Minh, Lan}`, `SeedData.Customers.{JaneSmith, NguyenVanAn}`, `SeedData.Vehicles.{JaneGolf, JaneTesla, AnVinfast}`; `DatabaseSeeder.SeedAsync(SchedulerDbContext db, CancellationToken cancellationToken)`.
  - `DependencyInjection.AddInfrastructure(this IServiceCollection services, Func<IServiceProvider, string> connectionString)` and `MigrateAndSeedAsync(this IServiceProvider services, CancellationToken cancellationToken)`.
  - Test fixture `PostgresFixture` with `ConnectionString`, `CreateDbContext()`, `ResetAppointmentsAsync(ct)`, `CancelAppointmentAsync(Guid, ct)`; collection `PostgresCollection.Name`.

- [ ] 5.1 Start Docker

Start Docker Desktop. Run: `docker info --format "{{.ServerVersion}}"`
Expected: a version number. If it fails, stop and ask the user to start Docker.

- [ ] 5.2 Create the Infrastructure project and the dotnet-ef tool

`src/Scheduler.Infrastructure/Scheduler.Infrastructure.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" PrivateAssets="all" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Scheduler.Application\Scheduler.Application.csproj" />
  </ItemGroup>
</Project>
```

```bash
dotnet sln VehicleServiceScheduler.slnx add src/Scheduler.Infrastructure/Scheduler.Infrastructure.csproj
dotnet new tool-manifest
dotnet tool install dotnet-ef --version 10.0.14
```

Expected: `dotnet-tools.json` is created at the repository root with `dotnet-ef` 10.0.14.

- [ ] 5.3 Create the integration test project and fixture

`tests/Scheduler.IntegrationTests/Scheduler.IntegrationTests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="xunit.v3" />
    <PackageReference Include="Testcontainers.PostgreSql" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\Scheduler.Infrastructure\Scheduler.Infrastructure.csproj" />
  </ItemGroup>
  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>
</Project>
```

```bash
dotnet sln VehicleServiceScheduler.slnx add tests/Scheduler.IntegrationTests/Scheduler.IntegrationTests.csproj
```

`tests/Scheduler.IntegrationTests/PostgresFixture.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Scheduler.Infrastructure.Persistence;
using Scheduler.Infrastructure.Seeding;
using Testcontainers.PostgreSql;

namespace Scheduler.IntegrationTests;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18").Build();

    public string ConnectionString => _container.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await _container.StartAsync(cancellationToken);
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync(cancellationToken);
        await DatabaseSeeder.SeedAsync(db, cancellationToken);
    }

    public ValueTask DisposeAsync() => _container.DisposeAsync();

    public SchedulerDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<SchedulerDbContext>().UseNpgsql(ConnectionString).Options);

    public async Task ResetAppointmentsAsync(CancellationToken cancellationToken)
    {
        await using var db = CreateDbContext();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE appointments", cancellationToken);
    }

    public async Task CancelAppointmentAsync(Guid appointmentId, CancellationToken cancellationToken)
    {
        await using var db = CreateDbContext();
        await db.Database.ExecuteSqlAsync($"UPDATE appointments SET status = 'Cancelled' WHERE id = {appointmentId}", cancellationToken);
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "Postgres";
}
```

- [ ] 5.4 Write the failing persistence tests

`tests/Scheduler.IntegrationTests/Persistence/BookingStoreTests.cs`:

```csharp
using Scheduler.Application.Booking;
using Scheduler.Domain.Appointments;
using Scheduler.Domain.Scheduling;
using Scheduler.Infrastructure.Persistence;
using Scheduler.Infrastructure.Seeding;

namespace Scheduler.IntegrationTests.Persistence;

[Collection(PostgresCollection.Name)]
public sealed class BookingStoreTests(PostgresFixture database) : IAsyncLifetime
{
    private static readonly DateTimeOffset Nine = new(2030, 1, 7, 9, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await database.ResetAppointmentsAsync(Ct);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Rejects_an_overlapping_appointment_for_the_same_bay()
    {
        await AddAsync(NewAppointment(SeedData.Bays.LondonBay1, SeedData.Technicians.Alice, SeedData.Vehicles.JaneGolf, Nine));

        var result = await AddAsync(NewAppointment(SeedData.Bays.LondonBay1, SeedData.Technicians.Ben, SeedData.Vehicles.JaneTesla, Nine.AddMinutes(30)));

        Assert.Equal(AddAppointmentResult.BayTaken, result);
    }

    [Fact]
    public async Task Rejects_an_overlapping_appointment_for_the_same_technician()
    {
        await AddAsync(NewAppointment(SeedData.Bays.LondonBay1, SeedData.Technicians.Alice, SeedData.Vehicles.JaneGolf, Nine));

        var result = await AddAsync(NewAppointment(SeedData.Bays.LondonBay2, SeedData.Technicians.Alice, SeedData.Vehicles.JaneTesla, Nine.AddMinutes(30)));

        Assert.Equal(AddAppointmentResult.TechnicianTaken, result);
    }

    [Fact]
    public async Task Rejects_an_overlapping_appointment_for_the_same_vehicle()
    {
        await AddAsync(NewAppointment(SeedData.Bays.LondonBay1, SeedData.Technicians.Alice, SeedData.Vehicles.JaneGolf, Nine));

        var result = await AddAsync(NewAppointment(SeedData.Bays.LondonBay2, SeedData.Technicians.Ben, SeedData.Vehicles.JaneGolf, Nine.AddMinutes(30)));

        Assert.Equal(AddAppointmentResult.VehicleTaken, result);
    }

    [Fact]
    public async Task Accepts_back_to_back_appointments_for_the_same_resources()
    {
        await AddAsync(NewAppointment(SeedData.Bays.LondonBay1, SeedData.Technicians.Alice, SeedData.Vehicles.JaneGolf, Nine));

        var result = await AddAsync(NewAppointment(SeedData.Bays.LondonBay1, SeedData.Technicians.Alice, SeedData.Vehicles.JaneGolf, Nine.AddHours(1)));

        Assert.Equal(AddAppointmentResult.Added, result);
    }

    [Fact]
    public async Task Ignores_cancelled_appointments()
    {
        var cancelled = NewAppointment(SeedData.Bays.LondonBay1, SeedData.Technicians.Alice, SeedData.Vehicles.JaneGolf, Nine);
        await AddAsync(cancelled);
        await database.CancelAppointmentAsync(cancelled.Id, Ct);

        var result = await AddAsync(NewAppointment(SeedData.Bays.LondonBay1, SeedData.Technicians.Alice, SeedData.Vehicles.JaneGolf, Nine));

        Assert.Equal(AddAppointmentResult.Added, result);
    }

    [Fact]
    public async Task Can_add_again_on_the_same_context_after_a_rejected_attempt()
    {
        await using var db = database.CreateDbContext();
        var store = new BookingStore(db);

        Assert.Equal(AddAppointmentResult.Added, await store.TryAddAsync(NewAppointment(SeedData.Bays.LondonBay1, SeedData.Technicians.Alice, SeedData.Vehicles.JaneGolf, Nine), Ct));
        Assert.Equal(AddAppointmentResult.BayTaken, await store.TryAddAsync(NewAppointment(SeedData.Bays.LondonBay1, SeedData.Technicians.Ben, SeedData.Vehicles.JaneTesla, Nine), Ct));
        Assert.Equal(AddAppointmentResult.Added, await store.TryAddAsync(NewAppointment(SeedData.Bays.LondonBay2, SeedData.Technicians.Ben, SeedData.Vehicles.JaneTesla, Nine), Ct));
    }

    [Fact]
    public async Task Loads_only_confirmed_appointments_that_overlap_the_period()
    {
        var overlapping = NewAppointment(SeedData.Bays.LondonBay1, SeedData.Technicians.Alice, SeedData.Vehicles.JaneGolf, Nine);
        var later = NewAppointment(SeedData.Bays.LondonBay1, SeedData.Technicians.Alice, SeedData.Vehicles.JaneGolf, Nine.AddHours(2));
        var cancelled = NewAppointment(SeedData.Bays.LondonBay2, SeedData.Technicians.Ben, SeedData.Vehicles.JaneTesla, Nine);
        foreach (var appointment in new[] { overlapping, later, cancelled })
            await AddAsync(appointment);
        await database.CancelAppointmentAsync(cancelled.Id, Ct);

        await using var db = database.CreateDbContext();
        var schedule = await new BookingStore(db).LoadScheduleAsync(
            SeedData.Dealerships.London,
            SeedData.Vehicles.JaneGolf,
            TimeRange.FromDuration(Nine, TimeSpan.FromHours(1)),
            Ct);

        Assert.Equal(overlapping.Id, Assert.Single(schedule.ConfirmedDealershipAppointments).Id);
        Assert.Equal(overlapping.Id, Assert.Single(schedule.ConfirmedVehicleAppointments).Id);
        Assert.Equal(4, schedule.Bays.Count);
        Assert.Equal(3, schedule.Technicians.Count);
        Assert.Equal(3, schedule.Technicians.Single(t => t.Id == SeedData.Technicians.Alice).Qualifications.Count);
    }

    [Fact]
    public async Task Loads_the_seeded_references_of_a_booking()
    {
        await using var db = database.CreateDbContext();
        var command = new BookAppointmentCommand(
            SeedData.Customers.JaneSmith,
            SeedData.Vehicles.JaneGolf,
            SeedData.Dealerships.London,
            SeedData.ServiceTypes.OilChange,
            Nine);

        var references = await new BookingStore(db).LoadReferencesAsync(command, Ct);

        Assert.True(references.CustomerExists);
        Assert.Equal(6, references.Dealership!.OpeningHours.Count);
        Assert.Equal(TimeSpan.FromMinutes(60), references.ServiceType!.Duration);
        Assert.Equal(SeedData.Customers.JaneSmith, references.Vehicle!.CustomerId);
    }

    private async Task<AddAppointmentResult> AddAsync(Appointment appointment)
    {
        await using var db = database.CreateDbContext();
        return await new BookingStore(db).TryAddAsync(appointment, Ct);
    }

    private static Appointment NewAppointment(Guid bayId, Guid technicianId, Guid vehicleId, DateTimeOffset start) =>
        Appointment.Confirm(
            Guid.CreateVersion7(),
            SeedData.Dealerships.London,
            SeedData.Customers.JaneSmith,
            vehicleId,
            SeedData.ServiceTypes.OilChange,
            bayId,
            technicianId,
            TimeRange.FromDuration(start, TimeSpan.FromHours(1)),
            start.AddDays(-30));
}
```

- [ ] 5.5 Run the tests and confirm they fail

Run: `dotnet test`
Expected: build fails with `CS0234`/`CS0246` errors for `Scheduler.Infrastructure.Persistence` and `Scheduler.Infrastructure.Seeding`.

- [ ] 5.6 Implement the DbContext and constraint names

`src/Scheduler.Infrastructure/Persistence/AppointmentConstraints.cs`:

```csharp
namespace Scheduler.Infrastructure.Persistence;

public static class AppointmentConstraints
{
    public const string BayNoOverlap = "ex_appointments_bay_no_overlap";
    public const string TechnicianNoOverlap = "ex_appointments_technician_no_overlap";
    public const string VehicleNoOverlap = "ex_appointments_vehicle_no_overlap";
}
```

`src/Scheduler.Infrastructure/Persistence/SchedulerDbContext.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Scheduler.Domain.Appointments;
using Scheduler.Domain.Catalog;
using Scheduler.Domain.Customers;
using Scheduler.Domain.Dealerships;
using Scheduler.Domain.Resources;

namespace Scheduler.Infrastructure.Persistence;

public sealed class SchedulerDbContext(DbContextOptions<SchedulerDbContext> options) : DbContext(options)
{
    public DbSet<Dealership> Dealerships => Set<Dealership>();

    public DbSet<ServiceType> ServiceTypes => Set<ServiceType>();

    public DbSet<ServiceBay> ServiceBays => Set<ServiceBay>();

    public DbSet<Technician> Technicians => Set<Technician>();

    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<Vehicle> Vehicles => Set<Vehicle>();

    public DbSet<Appointment> Appointments => Set<Appointment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("btree_gist");

        modelBuilder.Entity<Dealership>(dealership =>
        {
            dealership.ToTable("dealerships");
            dealership.Property(d => d.Name).HasMaxLength(200);
            dealership.Property(d => d.TimeZoneId).HasMaxLength(64);
            dealership.OwnsMany(d => d.OpeningHours, hours =>
            {
                hours.ToTable("opening_hours");
                hours.WithOwner().HasForeignKey("DealershipId");
                hours.HasKey("DealershipId", nameof(OpeningHours.Day));
            });
        });

        modelBuilder.Entity<ServiceType>(serviceType =>
        {
            serviceType.ToTable("service_types");
            serviceType.Property(s => s.Name).HasMaxLength(200);
            serviceType.Property(s => s.RequiredBayType).HasConversion<string>().HasMaxLength(32);
        });

        modelBuilder.Entity<ServiceBay>(bay =>
        {
            bay.ToTable("service_bays");
            bay.Property(b => b.Name).HasMaxLength(200);
            bay.Property(b => b.BayType).HasConversion<string>().HasMaxLength(32);
            bay.HasOne<Dealership>().WithMany().HasForeignKey(b => b.DealershipId);
        });

        modelBuilder.Entity<Technician>(technician =>
        {
            technician.ToTable("technicians");
            technician.Property(t => t.Name).HasMaxLength(200);
            technician.HasOne<Dealership>().WithMany().HasForeignKey(t => t.DealershipId);
            technician.OwnsMany(t => t.Qualifications, qualification =>
            {
                qualification.ToTable("technician_qualifications");
                qualification.WithOwner().HasForeignKey("TechnicianId");
                qualification.HasKey("TechnicianId", nameof(Qualification.ServiceTypeId));
                qualification.HasOne<ServiceType>().WithMany().HasForeignKey(q => q.ServiceTypeId);
            });
        });

        modelBuilder.Entity<Customer>(customer =>
        {
            customer.ToTable("customers");
            customer.Property(c => c.Name).HasMaxLength(200);
            customer.Property(c => c.Email).HasMaxLength(320);
        });

        modelBuilder.Entity<Vehicle>(vehicle =>
        {
            vehicle.ToTable("vehicles");
            vehicle.Property(v => v.Vin).HasMaxLength(17);
            vehicle.HasIndex(v => v.Vin).IsUnique();
            vehicle.Property(v => v.Make).HasMaxLength(100);
            vehicle.Property(v => v.Model).HasMaxLength(100);
            vehicle.HasOne<Customer>().WithMany().HasForeignKey(v => v.CustomerId);
        });

        modelBuilder.Entity<Appointment>(appointment =>
        {
            appointment.ToTable("appointments", table =>
                table.HasCheckConstraint("ck_appointments_end_after_start", "end_at > start_at"));
            appointment.Property(a => a.Status).HasConversion<string>().HasMaxLength(16);
            appointment.HasIndex(a => new { a.DealershipId, a.StartAt });
            appointment.HasOne<Dealership>().WithMany().HasForeignKey(a => a.DealershipId).OnDelete(DeleteBehavior.Restrict);
            appointment.HasOne<Customer>().WithMany().HasForeignKey(a => a.CustomerId).OnDelete(DeleteBehavior.Restrict);
            appointment.HasOne<Vehicle>().WithMany().HasForeignKey(a => a.VehicleId).OnDelete(DeleteBehavior.Restrict);
            appointment.HasOne<ServiceType>().WithMany().HasForeignKey(a => a.ServiceTypeId).OnDelete(DeleteBehavior.Restrict);
            appointment.HasOne<ServiceBay>().WithMany().HasForeignKey(a => a.ServiceBayId).OnDelete(DeleteBehavior.Restrict);
            appointment.HasOne<Technician>().WithMany().HasForeignKey(a => a.TechnicianId).OnDelete(DeleteBehavior.Restrict);
        });

        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
                property.SetColumnName(ToSnakeCase(property.Name));
        }
    }

    private static string ToSnakeCase(string name) =>
        string.Concat(name.Select((character, index) =>
            index > 0 && char.IsUpper(character)
                ? $"_{char.ToLowerInvariant(character)}"
                : char.ToLowerInvariant(character).ToString()));
}
```

`src/Scheduler.Infrastructure/Persistence/SchedulerDbContextFactory.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Scheduler.Infrastructure.Persistence;

public sealed class SchedulerDbContextFactory : IDesignTimeDbContextFactory<SchedulerDbContext>
{
    public SchedulerDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<SchedulerDbContext>().UseNpgsql("Host=localhost;Database=scheduler").Options);
}
```

- [ ] 5.7 Implement the seed data

`src/Scheduler.Infrastructure/Seeding/SeedData.cs`:

```csharp
namespace Scheduler.Infrastructure.Seeding;

public static class SeedData
{
    public static class Dealerships
    {
        public static readonly Guid London = Guid.Parse("d0000000-0000-4000-8000-000000000001");
        public static readonly Guid Saigon = Guid.Parse("d0000000-0000-4000-8000-000000000002");
    }

    public static class ServiceTypes
    {
        public static readonly Guid OilChange = Guid.Parse("5e000000-0000-4000-8000-000000000001");
        public static readonly Guid BrakeInspection = Guid.Parse("5e000000-0000-4000-8000-000000000002");
        public static readonly Guid WheelAlignment = Guid.Parse("5e000000-0000-4000-8000-000000000003");
        public static readonly Guid EvBatteryCheck = Guid.Parse("5e000000-0000-4000-8000-000000000004");
    }

    public static class Bays
    {
        public static readonly Guid LondonBay1 = Guid.Parse("ba000000-0000-4000-8000-000000000001");
        public static readonly Guid LondonBay2 = Guid.Parse("ba000000-0000-4000-8000-000000000002");
        public static readonly Guid LondonAlignmentBay = Guid.Parse("ba000000-0000-4000-8000-000000000003");
        public static readonly Guid LondonEvBay = Guid.Parse("ba000000-0000-4000-8000-000000000004");
        public static readonly Guid SaigonBayA = Guid.Parse("ba000000-0000-4000-8000-000000000005");
        public static readonly Guid SaigonBayB = Guid.Parse("ba000000-0000-4000-8000-000000000006");
    }

    public static class Technicians
    {
        public static readonly Guid Alice = Guid.Parse("7e000000-0000-4000-8000-000000000001");
        public static readonly Guid Ben = Guid.Parse("7e000000-0000-4000-8000-000000000002");
        public static readonly Guid Chloe = Guid.Parse("7e000000-0000-4000-8000-000000000003");
        public static readonly Guid Minh = Guid.Parse("7e000000-0000-4000-8000-000000000004");
        public static readonly Guid Lan = Guid.Parse("7e000000-0000-4000-8000-000000000005");
    }

    public static class Customers
    {
        public static readonly Guid JaneSmith = Guid.Parse("c0000000-0000-4000-8000-000000000001");
        public static readonly Guid NguyenVanAn = Guid.Parse("c0000000-0000-4000-8000-000000000002");
    }

    public static class Vehicles
    {
        public static readonly Guid JaneGolf = Guid.Parse("ca000000-0000-4000-8000-000000000001");
        public static readonly Guid JaneTesla = Guid.Parse("ca000000-0000-4000-8000-000000000002");
        public static readonly Guid AnVinfast = Guid.Parse("ca000000-0000-4000-8000-000000000003");
    }
}
```

`src/Scheduler.Infrastructure/Seeding/DatabaseSeeder.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Scheduler.Domain.Catalog;
using Scheduler.Domain.Customers;
using Scheduler.Domain.Dealerships;
using Scheduler.Domain.Resources;
using Scheduler.Infrastructure.Persistence;

namespace Scheduler.Infrastructure.Seeding;

public static class DatabaseSeeder
{
    public static async Task SeedAsync(SchedulerDbContext db, CancellationToken cancellationToken)
    {
        if (await db.Dealerships.AnyAsync(cancellationToken))
            return;

        db.Dealerships.AddRange(
            new Dealership(
                SeedData.Dealerships.London,
                "Riverside Motors London",
                "Europe/London",
                [.. Weekdays(new TimeOnly(8, 0), new TimeOnly(18, 0)), new OpeningHours(DayOfWeek.Saturday, new TimeOnly(9, 0), new TimeOnly(13, 0))]),
            new Dealership(
                SeedData.Dealerships.Saigon,
                "Saigon Auto Service",
                "Asia/Ho_Chi_Minh",
                [.. Weekdays(new TimeOnly(8, 0), new TimeOnly(17, 0)), new OpeningHours(DayOfWeek.Saturday, new TimeOnly(8, 0), new TimeOnly(17, 0))]));

        db.ServiceTypes.AddRange(
            new ServiceType(SeedData.ServiceTypes.OilChange, "Oil change", 60, BayType.General),
            new ServiceType(SeedData.ServiceTypes.BrakeInspection, "Brake inspection", 90, BayType.General),
            new ServiceType(SeedData.ServiceTypes.WheelAlignment, "Wheel alignment", 60, BayType.Alignment),
            new ServiceType(SeedData.ServiceTypes.EvBatteryCheck, "EV battery check", 120, BayType.ElectricVehicle));

        db.ServiceBays.AddRange(
            new ServiceBay(SeedData.Bays.LondonBay1, SeedData.Dealerships.London, "London Bay 1", BayType.General),
            new ServiceBay(SeedData.Bays.LondonBay2, SeedData.Dealerships.London, "London Bay 2", BayType.General),
            new ServiceBay(SeedData.Bays.LondonAlignmentBay, SeedData.Dealerships.London, "London Alignment Bay", BayType.Alignment),
            new ServiceBay(SeedData.Bays.LondonEvBay, SeedData.Dealerships.London, "London EV Bay", BayType.ElectricVehicle),
            new ServiceBay(SeedData.Bays.SaigonBayA, SeedData.Dealerships.Saigon, "Saigon Bay A", BayType.General),
            new ServiceBay(SeedData.Bays.SaigonBayB, SeedData.Dealerships.Saigon, "Saigon Bay B", BayType.General));

        db.Technicians.AddRange(
            new Technician(SeedData.Technicians.Alice, SeedData.Dealerships.London, "Alice Walker",
                [SeedData.ServiceTypes.OilChange, SeedData.ServiceTypes.BrakeInspection, SeedData.ServiceTypes.WheelAlignment]),
            new Technician(SeedData.Technicians.Ben, SeedData.Dealerships.London, "Ben Carter",
                [SeedData.ServiceTypes.OilChange, SeedData.ServiceTypes.BrakeInspection]),
            new Technician(SeedData.Technicians.Chloe, SeedData.Dealerships.London, "Chloe Evans",
                [SeedData.ServiceTypes.OilChange, SeedData.ServiceTypes.EvBatteryCheck]),
            new Technician(SeedData.Technicians.Minh, SeedData.Dealerships.Saigon, "Tran Van Minh",
                [SeedData.ServiceTypes.OilChange, SeedData.ServiceTypes.BrakeInspection]),
            new Technician(SeedData.Technicians.Lan, SeedData.Dealerships.Saigon, "Le Thi Lan",
                [SeedData.ServiceTypes.OilChange]));

        db.Customers.AddRange(
            new Customer(SeedData.Customers.JaneSmith, "Jane Smith", "jane.smith@example.com"),
            new Customer(SeedData.Customers.NguyenVanAn, "Nguyen Van An", "an.nguyen@example.com"));

        db.Vehicles.AddRange(
            new Vehicle(SeedData.Vehicles.JaneGolf, SeedData.Customers.JaneSmith, "WVWZZZ1KZ6W000001", "Volkswagen", "Golf", 2021),
            new Vehicle(SeedData.Vehicles.JaneTesla, SeedData.Customers.JaneSmith, "5YJ3E1EA7KF000002", "Tesla", "Model 3", 2023),
            new Vehicle(SeedData.Vehicles.AnVinfast, SeedData.Customers.NguyenVanAn, "RLLV3CAA5PV000003", "VinFast", "VF 8", 2024));

        await db.SaveChangesAsync(cancellationToken);
    }

    private static IEnumerable<OpeningHours> Weekdays(TimeOnly opens, TimeOnly closes) =>
        new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday }
            .Select(day => new OpeningHours(day, opens, closes));
}
```

- [ ] 5.8 Implement the stores and service registration

`src/Scheduler.Infrastructure/Persistence/BookingStore.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Scheduler.Application.Booking;
using Scheduler.Domain.Appointments;
using Scheduler.Domain.Scheduling;

namespace Scheduler.Infrastructure.Persistence;

public sealed class BookingStore(SchedulerDbContext db) : IBookingStore
{
    public async Task<BookingReferences> LoadReferencesAsync(BookAppointmentCommand command, CancellationToken cancellationToken)
    {
        var dealership = await db.Dealerships.AsNoTracking().SingleOrDefaultAsync(d => d.Id == command.DealershipId, cancellationToken);
        var serviceType = await db.ServiceTypes.AsNoTracking().SingleOrDefaultAsync(s => s.Id == command.ServiceTypeId, cancellationToken);
        var vehicle = await db.Vehicles.AsNoTracking().SingleOrDefaultAsync(v => v.Id == command.VehicleId, cancellationToken);
        var customerExists = await db.Customers.AnyAsync(c => c.Id == command.CustomerId, cancellationToken);
        return new BookingReferences(dealership, serviceType, vehicle, customerExists);
    }

    public async Task<ResourceSchedule> LoadScheduleAsync(Guid dealershipId, Guid vehicleId, TimeRange period, CancellationToken cancellationToken)
    {
        var start = period.Start;
        var end = period.End;
        var bays = await db.ServiceBays.AsNoTracking().Where(b => b.DealershipId == dealershipId).ToListAsync(cancellationToken);
        var technicians = await db.Technicians.AsNoTracking().Where(t => t.DealershipId == dealershipId).ToListAsync(cancellationToken);
        var dealershipAppointments = await ConfirmedOverlapping(start, end).Where(a => a.DealershipId == dealershipId).ToListAsync(cancellationToken);
        var vehicleAppointments = await ConfirmedOverlapping(start, end).Where(a => a.VehicleId == vehicleId).ToListAsync(cancellationToken);
        return new ResourceSchedule(bays, technicians, dealershipAppointments, vehicleAppointments);
    }

    public async Task<AddAppointmentResult> TryAddAsync(Appointment appointment, CancellationToken cancellationToken)
    {
        db.Appointments.Add(appointment);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return AddAppointmentResult.Added;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.ExclusionViolation } violation)
        {
            db.ChangeTracker.Clear();
            return violation.ConstraintName switch
            {
                AppointmentConstraints.BayNoOverlap => AddAppointmentResult.BayTaken,
                AppointmentConstraints.TechnicianNoOverlap => AddAppointmentResult.TechnicianTaken,
                AppointmentConstraints.VehicleNoOverlap => AddAppointmentResult.VehicleTaken,
                _ => throw new InvalidOperationException($"Unexpected exclusion constraint '{violation.ConstraintName}'.", exception),
            };
        }
    }

    private IQueryable<Appointment> ConfirmedOverlapping(DateTimeOffset start, DateTimeOffset end) =>
        db.Appointments.AsNoTracking().Where(a => a.Status == AppointmentStatus.Confirmed && a.StartAt < end && a.EndAt > start);
}
```

`src/Scheduler.Infrastructure/Persistence/AppointmentQueries.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Scheduler.Application.Appointments;

namespace Scheduler.Infrastructure.Persistence;

public sealed class AppointmentQueries(SchedulerDbContext db) : IAppointmentQueries
{
    public async Task<AppointmentDetails?> FindByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var row = await (
                from appointment in db.Appointments.AsNoTracking()
                join dealership in db.Dealerships on appointment.DealershipId equals dealership.Id
                join serviceType in db.ServiceTypes on appointment.ServiceTypeId equals serviceType.Id
                join bay in db.ServiceBays on appointment.ServiceBayId equals bay.Id
                join technician in db.Technicians on appointment.TechnicianId equals technician.Id
                where appointment.Id == id
                select new
                {
                    Appointment = appointment,
                    DealershipName = dealership.Name,
                    dealership.TimeZoneId,
                    ServiceTypeName = serviceType.Name,
                    serviceType.DurationMinutes,
                    BayName = bay.Name,
                    TechnicianName = technician.Name,
                })
            .SingleOrDefaultAsync(cancellationToken);

        if (row is null)
            return null;

        var a = row.Appointment;
        return new AppointmentDetails(
            a.Id,
            a.Status.ToString(),
            a.CustomerId,
            a.VehicleId,
            new DealershipSummary(a.DealershipId, row.DealershipName, row.TimeZoneId),
            new ServiceTypeSummary(a.ServiceTypeId, row.ServiceTypeName, row.DurationMinutes),
            new ResourceSummary(a.ServiceBayId, row.BayName),
            new ResourceSummary(a.TechnicianId, row.TechnicianName),
            a.StartAt,
            a.EndAt,
            a.CreatedAt);
    }
}
```

`src/Scheduler.Infrastructure/DependencyInjection.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Scheduler.Application.Appointments;
using Scheduler.Application.Booking;
using Scheduler.Infrastructure.Persistence;
using Scheduler.Infrastructure.Seeding;

namespace Scheduler.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, Func<IServiceProvider, string> connectionString)
    {
        services.AddDbContext<SchedulerDbContext>((provider, options) => options.UseNpgsql(connectionString(provider)));
        services.AddScoped<IBookingStore, BookingStore>();
        services.AddScoped<IAppointmentQueries, AppointmentQueries>();
        return services;
    }

    public static async Task MigrateAndSeedAsync(this IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SchedulerDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
        await DatabaseSeeder.SeedAsync(db, cancellationToken);
    }
}
```

- [ ] 5.9 Generate the migration and add the exclusion constraints

```bash
dotnet build src/Scheduler.Infrastructure
dotnet ef migrations add InitialSchema --project src/Scheduler.Infrastructure --startup-project src/Scheduler.Infrastructure --output-dir Persistence/Migrations
```

Expected: `Done.` and three new files in `src/Scheduler.Infrastructure/Persistence/Migrations/`. Check the generated `*_InitialSchema.cs`: it creates `btree_gist`, tables `dealerships`, `opening_hours`, `service_types`, `service_bays`, `technicians`, `technician_qualifications`, `customers`, `vehicles`, `appointments` with snake_case columns, and the check constraint `ck_appointments_end_after_start`.

In the generated `*_InitialSchema.cs`, add at the end of `Up`:

```csharp
            migrationBuilder.Sql($"""
                ALTER TABLE appointments ADD CONSTRAINT {AppointmentConstraints.BayNoOverlap}
                    EXCLUDE USING gist (service_bay_id WITH =, tstzrange(start_at, end_at, '[)') WITH &&)
                    WHERE (status = 'Confirmed');
                ALTER TABLE appointments ADD CONSTRAINT {AppointmentConstraints.TechnicianNoOverlap}
                    EXCLUDE USING gist (technician_id WITH =, tstzrange(start_at, end_at, '[)') WITH &&)
                    WHERE (status = 'Confirmed');
                ALTER TABLE appointments ADD CONSTRAINT {AppointmentConstraints.VehicleNoOverlap}
                    EXCLUDE USING gist (vehicle_id WITH =, tstzrange(start_at, end_at, '[)') WITH &&)
                    WHERE (status = 'Confirmed');
                """);
```

and at the start of `Down`:

```csharp
            migrationBuilder.Sql($"""
                ALTER TABLE appointments DROP CONSTRAINT {AppointmentConstraints.VehicleNoOverlap};
                ALTER TABLE appointments DROP CONSTRAINT {AppointmentConstraints.TechnicianNoOverlap};
                ALTER TABLE appointments DROP CONSTRAINT {AppointmentConstraints.BayNoOverlap};
                """);
```

If the generated file uses a block-scoped namespace other than `Scheduler.Infrastructure.Persistence.Migrations`, add `using Scheduler.Infrastructure.Persistence;` at the top.

- [ ] 5.10 Run the tests and confirm they pass

Run: `dotnet test`
Expected: unit tests 53 passed; integration tests 8 passed; failed 0. The first run pulls the `postgres:18` image and takes longer.

- [ ] 5.11 Commit

```bash
git add src tests dotnet-tools.json VehicleServiceScheduler.slnx openspec/changes/add-appointment-booking/tasks.md
git commit -m "feat(infrastructure): add PostgreSQL persistence with exclusion constraints and seed data" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

## 6. HTTP API, Problem Details and OpenAPI

**Files:**
- Create: `src/Scheduler.Api/Scheduler.Api.csproj`, `Program.cs`, `appsettings.json`, `appsettings.Development.json`, `Properties/launchSettings.json`
- Create: `src/Scheduler.Api/Problems/ProblemResults.cs`
- Create: `src/Scheduler.Api/Appointments/BookAppointmentRequest.cs`, `AppointmentEndpoints.cs`
- Modify: `tests/Scheduler.IntegrationTests/Scheduler.IntegrationTests.csproj`, `VehicleServiceScheduler.slnx`
- Create: `tests/Scheduler.IntegrationTests/Api/SchedulerApiFactory.cs`, `ApiTestHelpers.cs`, `BookAppointmentApiTests.cs`, `GetAppointmentApiTests.cs`, `ApiDocumentationTests.cs`

**Interfaces:**
- Consumes: `BookAppointmentHandler`, `BookAppointmentCommand`, `BookAppointmentResult`, `IAppointmentQueries`, `AppointmentDetails` (task 4); `AddInfrastructure`, `MigrateAndSeedAsync`, `SeedData`, `PostgresFixture` (task 5).
- Produces:
  - `POST /appointments` → 201 `AppointmentDetails` with `Location: /appointments/{id}`; 400/404/409/422 Problem Details.
  - `GET /appointments/{id}` → 200 `AppointmentDetails`; 404 Problem Details.
  - `GET /openapi/v1.json`, `GET /scalar`.
  - `public partial class Program` for `WebApplicationFactory<Program>`.
  - Test helpers: `SchedulerApiFactory(PostgresFixture database)`, `ApiTestHelpers.MondayNineUtc`, `BookingRequest(...)`, `JaneOilChangeInLondon(Guid vehicleId, string startAt = MondayNineUtc)`, extension methods `ReadJsonAsync()` and `AssertProblemAsync(int status, string slug)` on `HttpResponseMessage`.

- [ ] 6.1 Create the API project

`src/Scheduler.Api/Scheduler.Api.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.OpenApi" />
    <PackageReference Include="Scalar.AspNetCore" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Scheduler.Application\Scheduler.Application.csproj" />
    <ProjectReference Include="..\Scheduler.Infrastructure\Scheduler.Infrastructure.csproj" />
  </ItemGroup>
</Project>
```

`src/Scheduler.Api/appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "Database": {
    "MigrateOnStartup": false
  }
}
```

`src/Scheduler.Api/appsettings.Development.json` (development-only credentials that match the Compose defaults):

```json
{
  "ConnectionStrings": {
    "Scheduler": "Host=localhost;Port=5432;Database=scheduler;Username=scheduler;Password=scheduler"
  },
  "Database": {
    "MigrateOnStartup": true
  }
}
```

`src/Scheduler.Api/Properties/launchSettings.json`:

```json
{
  "$schema": "https://json.schemastore.org/launchsettings.json",
  "profiles": {
    "http": {
      "commandName": "Project",
      "launchBrowser": true,
      "launchUrl": "scalar",
      "applicationUrl": "http://localhost:8080",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development"
      }
    }
  }
}
```

In `tests/Scheduler.IntegrationTests/Scheduler.IntegrationTests.csproj`, add `<PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" />` to the package item group and `<ProjectReference Include="..\..\src\Scheduler.Api\Scheduler.Api.csproj" />` to the project-reference item group.

```bash
dotnet sln VehicleServiceScheduler.slnx add src/Scheduler.Api/Scheduler.Api.csproj
```

- [ ] 6.2 Write the test host and helpers

`tests/Scheduler.IntegrationTests/Api/SchedulerApiFactory.cs`:

```csharp
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Scheduler.IntegrationTests.Api;

public sealed class SchedulerApiFactory(PostgresFixture database) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Scheduler"] = database.ConnectionString,
            ["Database:MigrateOnStartup"] = "false",
        }));
    }
}
```

`tests/Scheduler.IntegrationTests/Api/ApiTestHelpers.cs`:

```csharp
using System.Net.Http.Json;
using System.Text.Json;
using Scheduler.Infrastructure.Seeding;

namespace Scheduler.IntegrationTests.Api;

internal static class ApiTestHelpers
{
    public const string MondayNineUtc = "2030-01-07T09:00:00Z";

    public static object BookingRequest(Guid customerId, Guid vehicleId, Guid dealershipId, Guid serviceTypeId, string startAt) =>
        new { customerId, vehicleId, dealershipId, serviceTypeId, startAt };

    public static object JaneOilChangeInLondon(Guid vehicleId, string startAt = MondayNineUtc) =>
        BookingRequest(SeedData.Customers.JaneSmith, vehicleId, SeedData.Dealerships.London, SeedData.ServiceTypes.OilChange, startAt);

    public static async Task<JsonElement> ReadJsonAsync(this HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

    public static async Task<JsonElement> AssertProblemAsync(this HttpResponseMessage response, int status, string slug)
    {
        Assert.Equal(status, (int)response.StatusCode);
        var problem = await response.ReadJsonAsync();
        Assert.Equal($"urn:vehicle-service-scheduler:problem:{slug}", problem.GetProperty("type").GetString());
        Assert.Equal(status, problem.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrEmpty(problem.GetProperty("traceId").GetString()));
        return problem;
    }
}
```

- [ ] 6.3 Write the failing API tests

`tests/Scheduler.IntegrationTests/Api/BookAppointmentApiTests.cs`:

```csharp
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Scheduler.Infrastructure.Seeding;
using static Scheduler.IntegrationTests.Api.ApiTestHelpers;

namespace Scheduler.IntegrationTests.Api;

[Collection(PostgresCollection.Name)]
public sealed class BookAppointmentApiTests(PostgresFixture database) : IAsyncLifetime
{
    private readonly SchedulerApiFactory _factory = new(database);
    private HttpClient _client = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await database.ResetAppointmentsAsync(Ct);
        _client = _factory.CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private Task<HttpResponseMessage> PostAsync(object request) =>
        _client.PostAsJsonAsync("/appointments", request, Ct);

    [Fact]
    public async Task Books_an_appointment_and_returns_it_with_the_assigned_resources()
    {
        var response = await PostAsync(JaneOilChangeInLondon(SeedData.Vehicles.JaneGolf));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.ReadJsonAsync();
        Assert.Equal($"/appointments/{body.GetProperty("id").GetGuid()}", response.Headers.Location?.OriginalString);
        Assert.Equal("Confirmed", body.GetProperty("status").GetString());
        Assert.Equal("London Bay 1", body.GetProperty("serviceBay").GetProperty("name").GetString());
        Assert.Equal("Alice Walker", body.GetProperty("technician").GetProperty("name").GetString());
        Assert.Equal("Europe/London", body.GetProperty("dealership").GetProperty("timeZone").GetString());
        Assert.Equal(DateTimeOffset.Parse("2030-01-07T09:00:00Z", CultureInfo.InvariantCulture), body.GetProperty("startAt").GetDateTimeOffset());
        Assert.Equal(DateTimeOffset.Parse("2030-01-07T10:00:00Z", CultureInfo.InvariantCulture), body.GetProperty("endAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task Assigns_the_same_bay_and_technician_to_a_back_to_back_booking()
    {
        await PostAsync(JaneOilChangeInLondon(SeedData.Vehicles.JaneGolf));

        var response = await PostAsync(JaneOilChangeInLondon(SeedData.Vehicles.JaneTesla, "2030-01-07T10:00:00Z"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.ReadJsonAsync();
        Assert.Equal(SeedData.Bays.LondonBay1, body.GetProperty("serviceBay").GetProperty("id").GetGuid());
        Assert.Equal(SeedData.Technicians.Alice, body.GetProperty("technician").GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Returns_409_with_a_bay_shortage_when_every_compatible_bay_is_taken()
    {
        await PostAsync(JaneOilChangeInLondon(SeedData.Vehicles.JaneGolf));
        await PostAsync(JaneOilChangeInLondon(SeedData.Vehicles.JaneTesla));

        var response = await PostAsync(BookingRequest(
            SeedData.Customers.NguyenVanAn, SeedData.Vehicles.AnVinfast, SeedData.Dealerships.London, SeedData.ServiceTypes.OilChange, MondayNineUtc));

        var problem = await response.AssertProblemAsync(409, "no-capacity");
        Assert.Equal("bays", problem.GetProperty("shortage").GetString());
    }

    [Fact]
    public async Task Returns_409_when_the_vehicle_already_has_an_overlapping_appointment()
    {
        await PostAsync(JaneOilChangeInLondon(SeedData.Vehicles.JaneGolf));

        var response = await PostAsync(BookingRequest(
            SeedData.Customers.JaneSmith, SeedData.Vehicles.JaneGolf, SeedData.Dealerships.London, SeedData.ServiceTypes.BrakeInspection, "2030-01-07T09:30:00Z"));

        await response.AssertProblemAsync(409, "vehicle-already-booked");
    }

    [Fact]
    public async Task Returns_422_when_the_vehicle_belongs_to_another_customer()
    {
        var response = await PostAsync(BookingRequest(
            SeedData.Customers.JaneSmith, SeedData.Vehicles.AnVinfast, SeedData.Dealerships.London, SeedData.ServiceTypes.OilChange, MondayNineUtc));

        await response.AssertProblemAsync(422, "vehicle-not-owned");
    }

    [Fact]
    public async Task Returns_422_when_the_start_time_is_in_the_past()
    {
        var response = await PostAsync(JaneOilChangeInLondon(SeedData.Vehicles.JaneGolf, "2020-01-06T09:00:00Z"));

        await response.AssertProblemAsync(422, "invalid-start-time");
    }

    [Fact]
    public async Task Returns_422_outside_opening_hours()
    {
        var response = await PostAsync(JaneOilChangeInLondon(SeedData.Vehicles.JaneGolf, "2030-01-06T10:00:00Z"));

        await response.AssertProblemAsync(422, "outside-opening-hours");
    }

    [Fact]
    public async Task Returns_422_when_the_dealership_cannot_offer_the_service()
    {
        var response = await PostAsync(BookingRequest(
            SeedData.Customers.NguyenVanAn, SeedData.Vehicles.AnVinfast, SeedData.Dealerships.Saigon, SeedData.ServiceTypes.WheelAlignment, "2030-01-07T02:00:00Z"));

        await response.AssertProblemAsync(422, "service-not-offered");
    }

    [Fact]
    public async Task Returns_404_for_an_unknown_dealership()
    {
        var response = await PostAsync(BookingRequest(
            SeedData.Customers.JaneSmith, SeedData.Vehicles.JaneGolf, Guid.NewGuid(), SeedData.ServiceTypes.OilChange, MondayNineUtc));

        await response.AssertProblemAsync(404, "not-found");
    }

    [Fact]
    public async Task Returns_400_when_the_start_time_has_no_offset()
    {
        var response = await PostAsync(JaneOilChangeInLondon(SeedData.Vehicles.JaneGolf, "2030-01-07T09:00:00"));

        var problem = await response.AssertProblemAsync(400, "validation");
        Assert.True(problem.GetProperty("errors").TryGetProperty("startAt", out _));
    }

    [Fact]
    public async Task Returns_400_with_an_error_per_missing_field()
    {
        var response = await PostAsync(new { });

        var problem = await response.AssertProblemAsync(400, "validation");
        Assert.Equal(5, problem.GetProperty("errors").EnumerateObject().Count());
    }
}
```

`tests/Scheduler.IntegrationTests/Api/GetAppointmentApiTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Scheduler.Infrastructure.Seeding;
using static Scheduler.IntegrationTests.Api.ApiTestHelpers;

namespace Scheduler.IntegrationTests.Api;

[Collection(PostgresCollection.Name)]
public sealed class GetAppointmentApiTests(PostgresFixture database) : IAsyncLifetime
{
    private readonly SchedulerApiFactory _factory = new(database);
    private HttpClient _client = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await database.ResetAppointmentsAsync(Ct);
        _client = _factory.CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task Returns_a_booked_appointment_from_its_location()
    {
        var created = await _client.PostAsJsonAsync("/appointments", JaneOilChangeInLondon(SeedData.Vehicles.JaneGolf), Ct);
        var createdBody = await created.ReadJsonAsync();

        var response = await _client.GetAsync(created.Headers.Location, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.ReadJsonAsync();
        Assert.Equal(createdBody.GetProperty("id").GetGuid(), body.GetProperty("id").GetGuid());
        Assert.Equal("Alice Walker", body.GetProperty("technician").GetProperty("name").GetString());
        Assert.Equal("Oil change", body.GetProperty("serviceType").GetProperty("name").GetString());
    }

    [Fact]
    public async Task Returns_404_for_an_unknown_appointment()
    {
        var response = await _client.GetAsync($"/appointments/{Guid.NewGuid()}", Ct);

        await response.AssertProblemAsync(404, "not-found");
    }
}
```

`tests/Scheduler.IntegrationTests/Api/ApiDocumentationTests.cs`:

```csharp
namespace Scheduler.IntegrationTests.Api;

[Collection(PostgresCollection.Name)]
public sealed class ApiDocumentationTests(PostgresFixture database) : IAsyncLifetime
{
    private readonly SchedulerApiFactory _factory = new(database);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Serves_the_openapi_document()
    {
        using var client = _factory.CreateClient();

        var document = await client.GetStringAsync("/openapi/v1.json", Ct);

        Assert.Contains("/appointments", document);
    }

    [Fact]
    public async Task Serves_the_scalar_ui()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/scalar", Ct);

        response.EnsureSuccessStatusCode();
    }
}
```

- [ ] 6.4 Run the tests and confirm they fail

Run: `dotnet test`
Expected: build fails because `Program` does not exist (`CS0246`).

- [ ] 6.5 Implement Problem Details helpers

`src/Scheduler.Api/Problems/ProblemResults.cs`:

```csharp
using System.Diagnostics;
using Microsoft.AspNetCore.Http.HttpResults;
using Scheduler.Domain.Scheduling;

namespace Scheduler.Api.Problems;

public static class ProblemResults
{
    private const string TypePrefix = "urn:vehicle-service-scheduler:problem:";

    public const string ValidationType = TypePrefix + "validation";

    public static Dictionary<string, object?> TraceExtensions(HttpContext httpContext) =>
        new() { ["traceId"] = Activity.Current?.TraceId.ToString() ?? httpContext.TraceIdentifier };

    public static ProblemHttpResult NotFound(HttpContext httpContext, string resource, Guid id) =>
        Create(httpContext, StatusCodes.Status404NotFound, "not-found", "Resource not found", $"The {resource} '{id}' was not found.");

    public static ProblemHttpResult RuleViolated(HttpContext httpContext, BookingRuleViolation violation) => violation switch
    {
        BookingRuleViolation.VehicleNotOwnedByCustomer => Unprocessable(httpContext, "vehicle-not-owned", "Vehicle not owned by customer",
            "The vehicle is not registered to the customer making the booking."),
        BookingRuleViolation.StartInPast => Unprocessable(httpContext, "invalid-start-time", "Invalid start time",
            "The start time must be in the future."),
        BookingRuleViolation.StartNotOnSlotBoundary => Unprocessable(httpContext, "invalid-start-time", "Invalid start time",
            "The start time must fall on a 15-minute boundary in the dealership's local time."),
        BookingRuleViolation.OutsideOpeningHours => Unprocessable(httpContext, "outside-opening-hours", "Outside opening hours",
            "The appointment must fit within the dealership's opening hours on a single day."),
        BookingRuleViolation.ServiceNotOffered => Unprocessable(httpContext, "service-not-offered", "Service not offered",
            "The dealership has no compatible bay or no qualified technician for this service type."),
        _ => throw new ArgumentOutOfRangeException(nameof(violation), violation, null),
    };

    public static ProblemHttpResult NoCapacity(HttpContext httpContext, CapacityShortage shortage)
    {
        var (code, detail) = shortage switch
        {
            CapacityShortage.Bays => ("bays", "No compatible bay is free for the whole requested time."),
            CapacityShortage.Technicians => ("technicians", "No qualified technician is free for the whole requested time."),
            CapacityShortage.BaysAndTechnicians => ("bays-and-technicians", "No compatible bay and no qualified technician are free for the whole requested time."),
            _ => throw new ArgumentOutOfRangeException(nameof(shortage), shortage, null),
        };
        var extensions = TraceExtensions(httpContext);
        extensions["shortage"] = code;
        return TypedResults.Problem(detail, statusCode: StatusCodes.Status409Conflict, title: "No capacity", type: TypePrefix + "no-capacity", extensions: extensions);
    }

    public static ProblemHttpResult VehicleAlreadyBooked(HttpContext httpContext) =>
        Create(httpContext, StatusCodes.Status409Conflict, "vehicle-already-booked", "Vehicle already booked",
            "The vehicle already has an appointment that overlaps the requested time.");

    private static ProblemHttpResult Unprocessable(HttpContext httpContext, string slug, string title, string detail) =>
        Create(httpContext, StatusCodes.Status422UnprocessableEntity, slug, title, detail);

    private static ProblemHttpResult Create(HttpContext httpContext, int status, string slug, string title, string detail) =>
        TypedResults.Problem(detail, statusCode: status, title: title, type: TypePrefix + slug, extensions: TraceExtensions(httpContext));
}
```

- [ ] 6.6 Implement the request model

`src/Scheduler.Api/Appointments/BookAppointmentRequest.cs`:

```csharp
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;
using Scheduler.Application.Booking;

namespace Scheduler.Api.Appointments;

public sealed partial record BookAppointmentRequest(
    Guid? CustomerId,
    Guid? VehicleId,
    Guid? DealershipId,
    Guid? ServiceTypeId,
    [property: Description("Start time in ISO 8601 with an explicit UTC offset, for example 2030-01-07T09:00:00Z.")]
    string? StartAt)
{
    public bool TryCreateCommand([NotNullWhen(true)] out BookAppointmentCommand? command, out Dictionary<string, string[]> errors)
    {
        errors = new Dictionary<string, string[]>();
        Require(CustomerId, "customerId", errors);
        Require(VehicleId, "vehicleId", errors);
        Require(DealershipId, "dealershipId", errors);
        Require(ServiceTypeId, "serviceTypeId", errors);
        var startAt = ParseStartAt(errors);

        if (errors.Count > 0 || startAt is null)
        {
            command = null;
            return false;
        }

        command = new BookAppointmentCommand(CustomerId!.Value, VehicleId!.Value, DealershipId!.Value, ServiceTypeId!.Value, startAt.Value);
        return true;
    }

    private DateTimeOffset? ParseStartAt(Dictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(StartAt))
        {
            errors["startAt"] = ["The startAt field is required."];
            return null;
        }

        if (!ExplicitOffset().IsMatch(StartAt)
            || !DateTimeOffset.TryParse(StartAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var startAt))
        {
            errors["startAt"] = ["The startAt field must be an ISO 8601 date-time with an explicit UTC offset, for example 2030-01-07T09:00:00Z."];
            return null;
        }

        return startAt.ToUniversalTime();
    }

    private static void Require(Guid? value, string field, Dictionary<string, string[]> errors)
    {
        if (value is null)
            errors[field] = [$"The {field} field is required."];
    }

    [GeneratedRegex(@"(Z|[+-]\d{2}:\d{2})$")]
    private static partial Regex ExplicitOffset();
}
```

- [ ] 6.7 Implement the endpoints

`src/Scheduler.Api/Appointments/AppointmentEndpoints.cs`:

```csharp
using Microsoft.AspNetCore.Http.HttpResults;
using Scheduler.Api.Problems;
using Scheduler.Application.Appointments;
using Scheduler.Application.Booking;

namespace Scheduler.Api.Appointments;

public static class AppointmentEndpoints
{
    public static IEndpointRouteBuilder MapAppointmentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/appointments").WithTags("Appointments");

        group.MapPost("/", BookAsync)
            .WithName("BookAppointment")
            .WithSummary("Book a service appointment")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/{id:guid}", GetAsync)
            .WithName("GetAppointment")
            .WithSummary("Get an appointment")
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<Results<Created<AppointmentDetails>, ValidationProblem, ProblemHttpResult>> BookAsync(
        BookAppointmentRequest request,
        BookAppointmentHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!request.TryCreateCommand(out var command, out var errors))
            return TypedResults.ValidationProblem(errors, type: ProblemResults.ValidationType, extensions: ProblemResults.TraceExtensions(httpContext));

        var result = await handler.HandleAsync(command, cancellationToken);
        return result switch
        {
            BookAppointmentResult.Booked booked => TypedResults.Created($"/appointments/{booked.Appointment.Id}", booked.Appointment),
            BookAppointmentResult.NotFound notFound => ProblemResults.NotFound(httpContext, notFound.Resource, notFound.Id),
            BookAppointmentResult.RuleViolated violated => ProblemResults.RuleViolated(httpContext, violated.Violation),
            BookAppointmentResult.NoCapacity noCapacity => ProblemResults.NoCapacity(httpContext, noCapacity.Shortage),
            BookAppointmentResult.VehicleAlreadyBooked => ProblemResults.VehicleAlreadyBooked(httpContext),
            _ => throw new InvalidOperationException($"Unhandled booking result '{result.GetType().Name}'."),
        };
    }

    private static async Task<Results<Ok<AppointmentDetails>, ProblemHttpResult>> GetAsync(
        Guid id,
        IAppointmentQueries queries,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var appointment = await queries.FindByIdAsync(id, cancellationToken);
        return appointment is null
            ? ProblemResults.NotFound(httpContext, "appointment", id)
            : TypedResults.Ok(appointment);
    }
}
```

- [ ] 6.8 Implement the composition root

`src/Scheduler.Api/Program.cs`:

```csharp
using Scalar.AspNetCore;
using Scheduler.Api.Appointments;
using Scheduler.Application.Booking;
using Scheduler.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddInfrastructure(services =>
    services.GetRequiredService<IConfiguration>().GetConnectionString("Scheduler")
    ?? throw new InvalidOperationException("Connection string 'Scheduler' is not configured."));
builder.Services.AddScoped<BookAppointmentHandler>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.MapOpenApi();
app.MapScalarApiReference();
app.MapAppointmentEndpoints();

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
    await app.Services.MigrateAndSeedAsync(app.Lifetime.ApplicationStopping);

await app.RunAsync();

public partial class Program;
```

- [ ] 6.9 Run the tests and confirm they pass

Run: `dotnet test`
Expected: unit tests 53 passed; integration tests 23 passed (8 persistence + 15 API); failed 0.

- [ ] 6.10 Commit

```bash
git add src tests VehicleServiceScheduler.slnx openspec/changes/add-appointment-booking/tasks.md
git commit -m "feat(api): add booking and retrieval endpoints with Problem Details and OpenAPI" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

## 7. Concurrency proof, container run, client stub and documentation

**Files:**
- Modify: `tests/Scheduler.IntegrationTests/PostgresFixture.cs`
- Create: `tests/Scheduler.IntegrationTests/Api/ConcurrentBookingTests.cs`
- Create: `Dockerfile`, `.dockerignore`, `docker-compose.yml`, `.env.example`
- Create: `http/scheduler.http`
- Modify: `README.md`, `PROJECT_STATE.md`

**Interfaces:**
- Consumes: everything above.
- Produces: `PostgresFixture.AddVehiclesAsync(Guid customerId, int count, CancellationToken)` and `CountOverlappingConfirmedAppointmentsAsync(CancellationToken)`; a running stack on `http://localhost:8080`.

- [ ] 7.1 Add the fixture helpers

In `tests/Scheduler.IntegrationTests/PostgresFixture.cs`, add `using Scheduler.Domain.Customers;` and these methods to `PostgresFixture`:

```csharp
    public async Task<IReadOnlyList<Guid>> AddVehiclesAsync(Guid customerId, int count, CancellationToken cancellationToken)
    {
        var vehicles = Enumerable.Range(0, count)
            .Select(_ => new Vehicle(Guid.CreateVersion7(), customerId, Guid.NewGuid().ToString("N")[..17].ToUpperInvariant(), "Test", "Car", 2030))
            .ToList();
        await using var db = CreateDbContext();
        db.Vehicles.AddRange(vehicles);
        await db.SaveChangesAsync(cancellationToken);
        return vehicles.Select(vehicle => vehicle.Id).ToList();
    }

    public async Task<int> CountOverlappingConfirmedAppointmentsAsync(CancellationToken cancellationToken)
    {
        await using var db = CreateDbContext();
        return await db.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value"
            FROM appointments a
            JOIN appointments b ON a.id < b.id
            WHERE a.status = 'Confirmed' AND b.status = 'Confirmed'
              AND (a.service_bay_id = b.service_bay_id OR a.technician_id = b.technician_id OR a.vehicle_id = b.vehicle_id)
              AND a.start_at < b.end_at AND b.start_at < a.end_at
            """).SingleAsync(cancellationToken);
    }
```

- [ ] 7.2 Write the concurrency test

`tests/Scheduler.IntegrationTests/Api/ConcurrentBookingTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Scheduler.Infrastructure.Seeding;
using static Scheduler.IntegrationTests.Api.ApiTestHelpers;

namespace Scheduler.IntegrationTests.Api;

[Collection(PostgresCollection.Name)]
public sealed class ConcurrentBookingTests(PostgresFixture database) : IAsyncLifetime
{
    private readonly SchedulerApiFactory _factory = new(database);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await database.ResetAppointmentsAsync(Ct);

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Parallel_requests_for_the_last_free_slots_never_double_book()
    {
        var vehicleIds = await database.AddVehiclesAsync(SeedData.Customers.JaneSmith, 12, Ct);
        using var client = _factory.CreateClient();

        var responses = await Task.WhenAll(vehicleIds.Select(vehicleId =>
            client.PostAsJsonAsync("/appointments", JaneOilChangeInLondon(vehicleId), Ct)));

        Assert.Equal(2, responses.Count(response => response.StatusCode == HttpStatusCode.Created));
        Assert.Equal(10, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(0, await database.CountOverlappingConfirmedAppointmentsAsync(Ct));
    }
}
```

- [ ] 7.3 Run the concurrency test

Run: `dotnet test`
Expected: PASS. The guarantee is already implemented in tasks 5–6; this test proves it end to end. Integration tests: 24 passed. If it fails, stop and use superpowers:systematic-debugging before changing any code.

- [ ] 7.4 Add the container image and Compose file

`Dockerfile`:

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/ src/
RUN dotnet publish src/Scheduler.Api/Scheduler.Api.csproj --configuration Release --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Scheduler.Api.dll"]
```

`.dockerignore`:

```
**/bin/
**/obj/
.git/
.github/
.claude/
docs/
http/
openspec/
tests/
*.md
.env
```

`docker-compose.yml`:

```yaml
services:
  postgres:
    image: postgres:18
    environment:
      POSTGRES_DB: scheduler
      POSTGRES_USER: scheduler
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD:-scheduler}
    ports:
      - "5432:5432"
    volumes:
      - postgres-data:/var/lib/postgresql
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U scheduler -d scheduler"]
      interval: 5s
      timeout: 3s
      retries: 10

  api:
    build: .
    environment:
      ConnectionStrings__Scheduler: "Host=postgres;Port=5432;Database=scheduler;Username=scheduler;Password=${POSTGRES_PASSWORD:-scheduler}"
      Database__MigrateOnStartup: "true"
    ports:
      - "8080:8080"
    depends_on:
      postgres:
        condition: service_healthy

volumes:
  postgres-data:
```

`.env.example`:

```
# Copy to .env to override the development defaults used by docker-compose.yml.
POSTGRES_PASSWORD=scheduler
```

- [ ] 7.5 Verify the running stack by hand

```bash
docker compose up --build -d
curl -s -o /dev/null -w "%{http_code}\n" http://localhost:8080/openapi/v1.json
```

Expected: `200` (retry for a few seconds while the API migrates and seeds).

```bash
curl -s -i -X POST http://localhost:8080/appointments -H "Content-Type: application/json" -d '{"customerId":"c0000000-0000-4000-8000-000000000001","vehicleId":"ca000000-0000-4000-8000-000000000001","dealershipId":"d0000000-0000-4000-8000-000000000001","serviceTypeId":"5e000000-0000-4000-8000-000000000001","startAt":"2030-07-01T07:00:00Z"}'
```

Expected: `HTTP/1.1 201 Created`, `Location: /appointments/<id>`, body with `"London Bay 1"` and `"Alice Walker"`. This booking is at 08:00 British Summer Time, so it also proves that time-zone data is present in the container.

```bash
curl -s -i http://localhost:8080/appointments/<id from the Location header>
curl -s -o /dev/null -w "%{http_code}\n" http://localhost:8080/scalar
docker compose down -v
```

Expected: `HTTP/1.1 200 OK` with the same appointment; `200` for Scalar; containers and the volume removed. Record the actual outputs in `PROJECT_STATE.md` under "Verification evidence".

- [ ] 7.6 Add the client stub

`http/scheduler.http`:

```http
@baseUrl = http://localhost:8080
@london = d0000000-0000-4000-8000-000000000001
@saigon = d0000000-0000-4000-8000-000000000002
@oilChange = 5e000000-0000-4000-8000-000000000001
@brakeInspection = 5e000000-0000-4000-8000-000000000002
@wheelAlignment = 5e000000-0000-4000-8000-000000000003
@jane = c0000000-0000-4000-8000-000000000001
@an = c0000000-0000-4000-8000-000000000002
@janeGolf = ca000000-0000-4000-8000-000000000001
@janeTesla = ca000000-0000-4000-8000-000000000002
@anVinfast = ca000000-0000-4000-8000-000000000003

### Book an oil change in London: 201, London Bay 1 with Alice Walker
# @name book
POST {{baseUrl}}/appointments
Content-Type: application/json

{
  "customerId": "{{jane}}",
  "vehicleId": "{{janeGolf}}",
  "dealershipId": "{{london}}",
  "serviceTypeId": "{{oilChange}}",
  "startAt": "2030-01-07T09:00:00Z"
}

### Read the appointment back: 200
GET {{baseUrl}}/appointments/{{book.response.body.$.id}}

### Second oil change at the same time: 201, London Bay 2 with Ben Carter
POST {{baseUrl}}/appointments
Content-Type: application/json

{
  "customerId": "{{jane}}",
  "vehicleId": "{{janeTesla}}",
  "dealershipId": "{{london}}",
  "serviceTypeId": "{{oilChange}}",
  "startAt": "2030-01-07T09:00:00Z"
}

### Third oil change at the same time: 409 no-capacity, shortage "bays"
POST {{baseUrl}}/appointments
Content-Type: application/json

{
  "customerId": "{{an}}",
  "vehicleId": "{{anVinfast}}",
  "dealershipId": "{{london}}",
  "serviceTypeId": "{{oilChange}}",
  "startAt": "2030-01-07T09:00:00Z"
}

### Same vehicle again, overlapping: 409 vehicle-already-booked
POST {{baseUrl}}/appointments
Content-Type: application/json

{
  "customerId": "{{jane}}",
  "vehicleId": "{{janeGolf}}",
  "dealershipId": "{{london}}",
  "serviceTypeId": "{{brakeInspection}}",
  "startAt": "2030-01-07T09:30:00Z"
}

### Wheel alignment in Saigon, which has no alignment bay: 422 service-not-offered
POST {{baseUrl}}/appointments
Content-Type: application/json

{
  "customerId": "{{an}}",
  "vehicleId": "{{anVinfast}}",
  "dealershipId": "{{saigon}}",
  "serviceTypeId": "{{wheelAlignment}}",
  "startAt": "2030-01-07T02:00:00Z"
}

### Sunday in London: 422 outside-opening-hours
POST {{baseUrl}}/appointments
Content-Type: application/json

{
  "customerId": "{{jane}}",
  "vehicleId": "{{janeTesla}}",
  "dealershipId": "{{london}}",
  "serviceTypeId": "{{oilChange}}",
  "startAt": "2030-01-06T10:00:00Z"
}

### Start time without an offset: 400 validation
POST {{baseUrl}}/appointments
Content-Type: application/json

{
  "customerId": "{{jane}}",
  "vehicleId": "{{janeTesla}}",
  "dealershipId": "{{london}}",
  "serviceTypeId": "{{oilChange}}",
  "startAt": "2030-01-07T11:00:00"
}
```

- [ ] 7.7 Update the README

In `README.md`:

1. In "Run everything with Docker Compose", remove the line `<!-- TODO: confirm ports once implemented -->` and replace the URL table with:

```markdown
| Service | URL |
| --- | --- |
| API | http://localhost:8080 |
| API docs (Scalar) | http://localhost:8080/scalar |
| OpenAPI document | http://localhost:8080/openapi/v1.json |
| Aspire Dashboard (traces, metrics, logs) | http://localhost:18888 (added by the `add-observability` change) |
```

and add after the table: ``Stop and remove everything, including the database volume, with `docker compose down -v`.``

2. In "Run the API locally", replace the command block with:

```bash
docker compose up -d postgres
dotnet run --project src/Scheduler.Api
```

and add: "The `Development` environment applies migrations and seeds sample data on start-up."

3. In "Running tests", add after the existing bullets: "Tests run on Microsoft.Testing.Platform (selected in `global.json`). Start Docker before running them: the integration tests start PostgreSQL 18 with Testcontainers."

4. In "API usage", remove the `<!-- TODO: update request/response examples once the API is implemented -->` line, add a column to the endpoint table so `POST /appointments` and `GET /appointments/{id}` show "Available" and the other two show "Planned (`add-cancellation-and-availability`)", replace the placeholder ids in the cURL example with the seeded ids used in step 7.5 (start time `2030-01-07T09:00:00Z`), and add this section after the example:

```markdown
### Sample data

| Kind | Name | Id |
| --- | --- | --- |
| Dealership | Riverside Motors London (Europe/London, Mon–Fri 08:00–18:00, Sat 09:00–13:00) | `d0000000-0000-4000-8000-000000000001` |
| Dealership | Saigon Auto Service (Asia/Ho_Chi_Minh, Mon–Sat 08:00–17:00) | `d0000000-0000-4000-8000-000000000002` |
| Service type | Oil change, 60 min, general bay | `5e000000-0000-4000-8000-000000000001` |
| Service type | Brake inspection, 90 min, general bay | `5e000000-0000-4000-8000-000000000002` |
| Service type | Wheel alignment, 60 min, alignment bay | `5e000000-0000-4000-8000-000000000003` |
| Service type | EV battery check, 120 min, EV bay | `5e000000-0000-4000-8000-000000000004` |
| Customer | Jane Smith (Volkswagen Golf `ca000000-…0001`, Tesla Model 3 `ca000000-…0002`) | `c0000000-0000-4000-8000-000000000001` |
| Customer | Nguyen Van An (VinFast VF 8 `ca000000-…0003`) | `c0000000-0000-4000-8000-000000000002` |

London has two general bays, one alignment bay and one EV bay, and three technicians; Saigon has two general bays and two technicians and offers no wheel alignment or EV battery check.
```

5. Remove `<!-- TODO: add CI badge once .github/workflows/ci.yml exists -->` only if the repository has a GitHub remote; otherwise leave it.

- [ ] 7.8 Run the full verification

```bash
dotnet build --configuration Release
dotnet test --configuration Release
```

Expected: build with 0 warnings and 0 errors; unit tests 53 passed; integration tests 24 passed; failed 0. Paste the summaries into `PROJECT_STATE.md` under "Verification evidence".

- [ ] 7.9 Review the change

Use superpowers:requesting-code-review (or the `senior-reviewer` agent) on the diff from `docs/system-design` to `HEAD`. Check every spec scenario in `specs/appointment-booking/spec.md` has a test. Fix confirmed findings with a failing test first; record any finding that is consciously accepted in `PROJECT_STATE.md` under "Known issues".

- [ ] 7.10 Update the project state and commit

In `PROJECT_STATE.md`: set the TASK-002 row to `DONE` with the verification evidence, release its file claims, add the work to "Completed work", and set "Next task" to the `add-cancellation-and-availability` change.

```bash
git add tests Dockerfile .dockerignore docker-compose.yml .env.example http README.md PROJECT_STATE.md openspec/changes/add-appointment-booking/tasks.md
git commit -m "feat: prove concurrent booking safety and add container run, client stub and docs" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

- [ ] 7.11 Hand over to the user

Report the verification evidence and ask the user whether to merge `feature/appointment-booking` and then archive the change with `openspec archive add-appointment-booking -y`. Do not merge, push or archive without that approval.
