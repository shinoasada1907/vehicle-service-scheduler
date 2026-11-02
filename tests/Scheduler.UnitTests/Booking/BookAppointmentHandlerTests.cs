using Scheduler.Application.Booking;
using Scheduler.Domain.Appointments;
using Scheduler.Domain.Scheduling;

namespace Scheduler.UnitTests.Booking;

public sealed class BookAppointmentHandlerTests
{
    private static readonly Guid OtherDealershipId = Guid.Parse("d0000000-0000-4000-8000-000000000002");

    [Fact]
    public async Task S1_OilChangeOnMonday_IsConfirmedWithFirstBayAndTechnician()
    {
        var scenario = new BookingScenario();

        var result = await scenario.Handler().HandleAsync(scenario.Command(), TestContext.Current.CancellationToken);

        var booked = Assert.IsType<BookAppointmentResult.Booked>(result);
        Assert.Equal("Confirmed", booked.Appointment.Status);
        Assert.Equal("Bay 1", booked.Appointment.ServiceBay.Name);
        Assert.Equal("Alice", booked.Appointment.Technician.Name);
        Assert.Equal(TestData.Utc("2030-01-07T09:00:00Z"), booked.Appointment.StartAt);
        Assert.Equal(TestData.Utc("2030-01-07T10:00:00Z"), booked.Appointment.EndAt);
        Assert.Equal("Europe/London", booked.Appointment.Dealership.TimeZone);
        Assert.Single(scenario.Store.Appointments);
    }

    [Fact]
    public async Task S2_BrakeInspection_EndsNinetyMinutesAfterStart()
    {
        var scenario = new BookingScenario();
        var command = scenario.Command(serviceType: TestData.BrakeInspection);

        var result = await scenario.Handler().HandleAsync(command, TestContext.Current.CancellationToken);

        var booked = Assert.IsType<BookAppointmentResult.Booked>(result);
        Assert.Equal(TestData.Utc("2030-01-07T10:30:00Z"), booked.Appointment.EndAt);
    }

    [Fact]
    public async Task S3_StartWithOffset_IsStoredInUtc()
    {
        var scenario = new BookingScenario();
        var command = scenario.Command("2030-01-07T10:00:00+01:00");

        var result = await scenario.Handler().HandleAsync(command, TestContext.Current.CancellationToken);

        var booked = Assert.IsType<BookAppointmentResult.Booked>(result);
        Assert.Equal(TestData.Utc("2030-01-07T09:00:00Z"), booked.Appointment.StartAt);
        Assert.Equal(TimeSpan.Zero, booked.Appointment.StartAt.Offset);
    }

    [Theory]
    [InlineData("dealership")]
    [InlineData("serviceType")]
    [InlineData("customer")]
    [InlineData("vehicle")]
    public async Task S22_UnknownReference_IsNotFound(string resource)
    {
        var scenario = new BookingScenario();
        var command = scenario.Command();
        var unknownId = Guid.NewGuid();
        var commandWithUnknownReference = resource switch
        {
            "dealership" => command with { DealershipId = unknownId },
            "serviceType" => command with { ServiceTypeId = unknownId },
            "customer" => command with { CustomerId = unknownId },
            "vehicle" => command with { VehicleId = unknownId },
            _ => throw new ArgumentOutOfRangeException(nameof(resource)),
        };

        var result = await scenario.Handler().HandleAsync(
            commandWithUnknownReference, TestContext.Current.CancellationToken);

        var notFound = Assert.IsType<BookAppointmentResult.NotFound>(result);
        Assert.Equal(resource, notFound.Resource);
    }

    [Fact]
    public async Task S27_BayTakenByConcurrentRequest_BooksOtherBayAndTechnician()
    {
        var scenario = new BookingScenario();
        var winner = TestData.Booked(scenario.Bay1, scenario.Alice, "2030-01-07T09:00:00Z");
        scenario.Store.Conflicts.Enqueue((AddAppointmentResult.BayTaken, winner));

        var result = await scenario.Handler().HandleAsync(scenario.Command(), TestContext.Current.CancellationToken);

        var booked = Assert.IsType<BookAppointmentResult.Booked>(result);
        Assert.Equal("Bay 2", booked.Appointment.ServiceBay.Name);
        Assert.Equal("Ben", booked.Appointment.Technician.Name);
        Assert.Equal(2, scenario.Store.ScheduleLoads);
    }

    [Fact]
    public async Task S28_LosingEveryAttempt_ReportsNoCapacity()
    {
        var scenario = new BookingScenario();
        for (var attempt = 0; attempt < BookAppointmentHandler.MaxAttempts; attempt++)
        {
            scenario.Store.Conflicts.Enqueue((AddAppointmentResult.TechnicianTaken, null));
        }

        var result = await scenario.Handler().HandleAsync(scenario.Command(), TestContext.Current.CancellationToken);

        var noCapacity = Assert.IsType<BookAppointmentResult.NoCapacity>(result);
        Assert.Equal(CapacityShortage.Technicians, noCapacity.Shortage);
        Assert.Equal(BookAppointmentHandler.MaxAttempts, scenario.Store.ScheduleLoads);
    }

    [Fact]
    public async Task E7_DeadlockOnFirstAttempt_RetriesAndBooks()
    {
        var scenario = new BookingScenario();
        scenario.Store.Conflicts.Enqueue((AddAppointmentResult.Deadlocked, null));

        var result = await scenario.Handler().HandleAsync(scenario.Command(), TestContext.Current.CancellationToken);

        Assert.IsType<BookAppointmentResult.Booked>(result);
        Assert.Equal(2, scenario.Store.ScheduleLoads);
    }

    [Fact]
    public async Task S30_VehicleBookedAtAnotherDealershipAtSameTime_IsRejected()
    {
        var scenario = new BookingScenario();
        var existing = Appointment.Confirm(
            Guid.NewGuid(),
            OtherDealershipId,
            TestData.CustomerId,
            scenario.Vehicle.Id,
            TestData.OilChange.Id,
            Guid.NewGuid(),
            Guid.NewGuid(),
            TimeRange.FromDuration(TestData.Utc("2030-01-07T09:00:00Z"), TimeSpan.FromHours(1)),
            TestData.Now);
        scenario.Store.Appointments.Add(existing);
        var command = scenario.Command("2030-01-07T09:30:00Z");

        var result = await scenario.Handler().HandleAsync(command, TestContext.Current.CancellationToken);

        Assert.IsType<BookAppointmentResult.VehicleAlreadyBooked>(result);
    }

    [Fact]
    public async Task S30_VehicleTakenByConcurrentRequest_IsRejectedWithoutRetry()
    {
        var scenario = new BookingScenario();
        scenario.Store.Conflicts.Enqueue((AddAppointmentResult.VehicleTaken, null));

        var result = await scenario.Handler().HandleAsync(scenario.Command(), TestContext.Current.CancellationToken);

        Assert.IsType<BookAppointmentResult.VehicleAlreadyBooked>(result);
        Assert.Equal(1, scenario.Store.ScheduleLoads);
    }
}
