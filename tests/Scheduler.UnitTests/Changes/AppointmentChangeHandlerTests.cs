using Scheduler.Application.Changes;
using Scheduler.Domain.Scheduling;

namespace Scheduler.UnitTests.Changes;

public sealed class AppointmentChangeHandlerTests
{
    [Fact]
    public async Task R1_MoveToFreeTime_ReturnsNewTimesAndChosenResources()
    {
        var scenario = new ChangeScenario();

        var result = await scenario.Reschedule().HandleAsync(
            scenario.Appointment.Id, TestData.Utc("2030-01-07T11:00:00Z"), TestContext.Current.CancellationToken);

        var rescheduled = Assert.IsType<RescheduleAppointmentResult.Rescheduled>(result);
        Assert.Equal(scenario.Appointment.Id, rescheduled.Appointment.Id);
        Assert.Equal(TestData.Utc("2030-01-07T11:00:00Z"), rescheduled.Appointment.StartAt);
        Assert.Equal(TestData.Utc("2030-01-07T12:00:00Z"), rescheduled.Appointment.EndAt);
        Assert.Equal("Bay 1", rescheduled.Appointment.ServiceBay.Name);
        Assert.Equal("Alice", rescheduled.Appointment.Technician.Name);
        Assert.Single(scenario.Store.Moves);
    }

    [Fact]
    public async Task R3_NoCapacityAtNewTime_ReportsNoCapacityAndRecordsNoMove()
    {
        var scenario = new ChangeScenario();
        scenario.Store.Appointments.Add(TestData.Booked(scenario.Bay1, scenario.Alice, "2030-01-07T11:00:00Z"));
        scenario.Store.Appointments.Add(TestData.Booked(scenario.Bay2, scenario.Ben, "2030-01-07T11:00:00Z"));

        var result = await scenario.Reschedule().HandleAsync(
            scenario.Appointment.Id, TestData.Utc("2030-01-07T11:00:00Z"), TestContext.Current.CancellationToken);

        Assert.IsType<RescheduleAppointmentResult.NoCapacity>(result);
        Assert.Empty(scenario.Store.Moves);
        Assert.Equal(TestData.Utc("2030-01-07T09:00:00Z"), scenario.Appointment.StartAt);
    }

    [Fact]
    public async Task R4_VehicleHasOverlappingAppointment_IsRejected()
    {
        var scenario = new ChangeScenario();
        scenario.Store.Appointments.Add(
            TestData.Booked(scenario.Bay2, scenario.Ben, "2030-01-07T11:30:00Z", vehicleId: scenario.VehicleId));

        var result = await scenario.Reschedule().HandleAsync(
            scenario.Appointment.Id, TestData.Utc("2030-01-07T11:00:00Z"), TestContext.Current.CancellationToken);

        Assert.IsType<RescheduleAppointmentResult.VehicleAlreadyBooked>(result);
        Assert.Empty(scenario.Store.Moves);
    }

    [Theory]
    [InlineData("2030-01-07T11:10:00Z", BookingRuleViolation.StartNotOnSlotBoundary)]
    [InlineData("2030-01-07T19:00:00Z", BookingRuleViolation.OutsideOpeningHours)]
    public async Task R5_NewStartBreaksBookingRule_IsRuleViolated(string newStart, BookingRuleViolation expected)
    {
        var scenario = new ChangeScenario();

        var result = await scenario.Reschedule().HandleAsync(
            scenario.Appointment.Id, TestData.Utc(newStart), TestContext.Current.CancellationToken);

        var violated = Assert.IsType<RescheduleAppointmentResult.RuleViolated>(result);
        Assert.Equal(expected, violated.Violation);
        Assert.Empty(scenario.Store.Moves);
    }

    [Fact]
    public async Task R6_StartsInTwoHoursFifteenMinutes_CanBeMoved()
    {
        var scenario = new ChangeScenario();
        scenario.Clock.SetUtcNow(scenario.Appointment.StartAt - TimeSpan.FromMinutes(135));

        var result = await scenario.Reschedule().HandleAsync(
            scenario.Appointment.Id, TestData.Utc("2030-01-07T11:00:00Z"), TestContext.Current.CancellationToken);

        Assert.IsType<RescheduleAppointmentResult.Rescheduled>(result);
    }

    [Fact]
    public async Task R6_StartsInExactlyTwoHours_IsChangeWindowClosed()
    {
        var scenario = new ChangeScenario();
        scenario.Clock.SetUtcNow(scenario.Appointment.StartAt - TimeSpan.FromHours(2));

        var result = await scenario.Reschedule().HandleAsync(
            scenario.Appointment.Id, TestData.Utc("2030-01-07T11:00:00Z"), TestContext.Current.CancellationToken);

        var violated = Assert.IsType<RescheduleAppointmentResult.ChangeRuleViolated>(result);
        Assert.Equal(ChangeRuleViolation.ChangeWindowClosed, violated.Violation);
        Assert.Empty(scenario.Store.Moves);
    }

    [Fact]
    public async Task R7_CancelledAppointment_IsAppointmentCancelled()
    {
        var scenario = new ChangeScenario();
        scenario.Appointment.Cancel(TestData.Now);

        var result = await scenario.Reschedule().HandleAsync(
            scenario.Appointment.Id, TestData.Utc("2030-01-07T11:00:00Z"), TestContext.Current.CancellationToken);

        var violated = Assert.IsType<RescheduleAppointmentResult.ChangeRuleViolated>(result);
        Assert.Equal(ChangeRuleViolation.AppointmentCancelled, violated.Violation);
    }

    [Fact]
    public async Task R8_SameTime_ReturnsCurrentStateWithoutMoving()
    {
        var scenario = new ChangeScenario();

        var result = await scenario.Reschedule().HandleAsync(
            scenario.Appointment.Id, scenario.Appointment.StartAt, TestContext.Current.CancellationToken);

        var rescheduled = Assert.IsType<RescheduleAppointmentResult.Rescheduled>(result);
        Assert.Equal(scenario.Appointment.StartAt, rescheduled.Appointment.StartAt);
        Assert.Empty(scenario.Store.Moves);
    }

    [Fact]
    public async Task C1_CancelInTime_ReturnsCancelledWithCancellationTime()
    {
        var scenario = new ChangeScenario();

        var result = await scenario.Cancel().HandleAsync(scenario.Appointment.Id, TestContext.Current.CancellationToken);

        var cancelled = Assert.IsType<CancelAppointmentResult.Cancelled>(result);
        Assert.Equal("Cancelled", cancelled.Appointment.Status);
        Assert.Equal(TestData.Now, cancelled.Appointment.CancelledAt);
    }

    [Fact]
    public async Task C3_CancelAlreadyCancelled_ReturnsUnchangedCancellationTime()
    {
        var scenario = new ChangeScenario();
        scenario.Appointment.Cancel(TestData.Now);
        scenario.Clock.Advance(TimeSpan.FromHours(1));

        var result = await scenario.Cancel().HandleAsync(scenario.Appointment.Id, TestContext.Current.CancellationToken);

        var cancelled = Assert.IsType<CancelAppointmentResult.Cancelled>(result);
        Assert.Equal(TestData.Now, cancelled.Appointment.CancelledAt);
    }

    [Fact]
    public async Task C4_CancelWithinCutoff_IsChangeWindowClosed()
    {
        var scenario = new ChangeScenario();
        scenario.Clock.SetUtcNow(scenario.Appointment.StartAt - TimeSpan.FromHours(2));

        var result = await scenario.Cancel().HandleAsync(scenario.Appointment.Id, TestContext.Current.CancellationToken);

        var violated = Assert.IsType<CancelAppointmentResult.ChangeRuleViolated>(result);
        Assert.Equal(ChangeRuleViolation.ChangeWindowClosed, violated.Violation);
        Assert.Equal("Confirmed", scenario.Appointment.Status.ToString());
    }

    [Fact]
    public async Task A4_AlternativesForCancelledAppointment_IsAppointmentCancelled()
    {
        var scenario = new ChangeScenario();
        scenario.Appointment.Cancel(TestData.Now);

        var result = await scenario.Alternatives().HandleAsync(
            scenario.Appointment.Id, null, TestContext.Current.CancellationToken);

        var violated = Assert.IsType<AvailabilityLookupResult.ChangeRuleViolated>(result);
        Assert.Equal(ChangeRuleViolation.AppointmentCancelled, violated.Violation);
    }

    [Fact]
    public async Task A4_AlternativesWithinCutoff_IsChangeWindowClosed()
    {
        var scenario = new ChangeScenario();
        scenario.Clock.SetUtcNow(scenario.Appointment.StartAt - TimeSpan.FromHours(1));

        var result = await scenario.Alternatives().HandleAsync(
            scenario.Appointment.Id, null, TestContext.Current.CancellationToken);

        var violated = Assert.IsType<AvailabilityLookupResult.ChangeRuleViolated>(result);
        Assert.Equal(ChangeRuleViolation.ChangeWindowClosed, violated.Violation);
    }

    [Fact]
    public async Task A4_AlternativesForUnknownId_IsNotFound()
    {
        var scenario = new ChangeScenario();

        var result = await scenario.Alternatives().HandleAsync(
            Guid.NewGuid(), null, TestContext.Current.CancellationToken);

        Assert.IsType<AvailabilityLookupResult.NotFound>(result);
    }
}
