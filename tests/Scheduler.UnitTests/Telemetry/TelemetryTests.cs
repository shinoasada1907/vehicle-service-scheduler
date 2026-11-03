using Scheduler.Application.Booking;
using Scheduler.Application.Changes;
using Scheduler.UnitTests.Booking;
using Scheduler.UnitTests.Changes;

namespace Scheduler.UnitTests.Telemetry;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TelemetryCollection
{
    public const string Name = "Telemetry";
}

[Collection(TelemetryCollection.Name)]
public sealed class TelemetryTests
{
    [Fact]
    public async Task O4_BookingOutcomes_AreCountedByOutcome()
    {
        using var capture = new TelemetryCapture();
        var confirmed = new BookingScenario();
        var busy = new BookingScenario();
        var otherTechnician = TestData.NewTechnician("Zoe", TestData.OilChange);
        busy.Store.Appointments.Add(TestData.Booked(busy.Bay1, otherTechnician, "2030-01-07T09:00:00Z"));
        busy.Store.Appointments.Add(TestData.Booked(busy.Bay2, otherTechnician, "2030-01-07T09:00:00Z"));

        await confirmed.Handler().HandleAsync(confirmed.Command(), TestContext.Current.CancellationToken);
        await busy.Handler().HandleAsync(busy.Command(), TestContext.Current.CancellationToken);

        var bookings = capture.Measurements("scheduler.bookings");
        Assert.Equal(2, bookings.Count);
        Assert.Single(bookings, measurement => measurement.Value == 1 && measurement.Tags["outcome"] == "confirmed");
        Assert.Single(bookings, measurement => measurement.Value == 1 && measurement.Tags["outcome"] == "no-capacity-bays");
    }

    [Fact]
    public async Task O5_LostRaceForBay_IsCountedAsBayConflict()
    {
        using var capture = new TelemetryCapture();
        var scenario = new BookingScenario();
        var winner = TestData.Booked(scenario.Bay1, scenario.Alice, "2030-01-07T09:00:00Z");
        scenario.Store.Conflicts.Enqueue((AddAppointmentResult.BayTaken, winner));

        await scenario.Handler().HandleAsync(scenario.Command(), TestContext.Current.CancellationToken);

        var conflict = Assert.Single(capture.Measurements("scheduler.booking.conflicts"));
        Assert.Equal(1, conflict.Value);
        Assert.Equal("bay", conflict.Tags["resource"]);
    }

    [Fact]
    public async Task O6_CancelAndReschedule_AreCountedByOutcome()
    {
        using var capture = new TelemetryCapture();
        var cancelScenario = new ChangeScenario();
        var rescheduleScenario = new ChangeScenario();

        await cancelScenario.Cancel().HandleAsync(
            cancelScenario.Appointment.Id, TestContext.Current.CancellationToken);
        await rescheduleScenario.Reschedule().HandleAsync(
            rescheduleScenario.Appointment.Id,
            TestData.Utc("2030-01-07T11:00:00Z"),
            TestContext.Current.CancellationToken);

        var cancellation = Assert.Single(capture.Measurements("scheduler.cancellations"));
        Assert.Equal(1, cancellation.Value);
        Assert.Equal("cancelled", cancellation.Tags["outcome"]);
        var reschedule = Assert.Single(capture.Measurements("scheduler.reschedules"));
        Assert.Equal(1, reschedule.Value);
        Assert.Equal("rescheduled", reschedule.Tags["outcome"]);
    }

    [Fact]
    public async Task O7_BookingWithOneLostRace_ProducesActivityWithConflictEvent()
    {
        using var capture = new TelemetryCapture();
        var scenario = new BookingScenario();
        var winner = TestData.Booked(scenario.Bay1, scenario.Alice, "2030-01-07T09:00:00Z");
        scenario.Store.Conflicts.Enqueue((AddAppointmentResult.BayTaken, winner));

        await scenario.Handler().HandleAsync(scenario.Command(), TestContext.Current.CancellationToken);

        var activity = Assert.Single(capture.Activities("scheduler.book"));
        Assert.Equal("confirmed", activity.GetTagItem("scheduler.outcome"));
        Assert.Equal(2, activity.GetTagItem("scheduler.attempts"));
        var conflictEvent = Assert.Single(activity.Events);
        Assert.Equal("conflict", conflictEvent.Name);
        Assert.Equal("bay", Assert.Single(conflictEvent.Tags, tag => tag.Key == "resource").Value);
    }
}
