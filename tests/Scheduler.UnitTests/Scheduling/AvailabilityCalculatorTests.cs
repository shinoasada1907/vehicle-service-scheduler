using Scheduler.Domain.Appointments;
using Scheduler.Domain.Resources;
using Scheduler.Domain.Scheduling;

namespace Scheduler.UnitTests.Scheduling;

public sealed class AvailabilityCalculatorTests
{
    private static readonly DateOnly Monday = new(2030, 1, 7);

    private static IReadOnlyList<TimeRange> Calculate(
        DateOnly date,
        DateTimeOffset now,
        IReadOnlyCollection<ServiceBay> bays,
        IReadOnlyCollection<Technician> technicians,
        IReadOnlyCollection<Appointment>? dealershipAppointments = null,
        IReadOnlyCollection<Appointment>? vehicleAppointments = null,
        Guid? ignoredAppointmentId = null)
    {
        var result = AvailabilityCalculator.Calculate(
            TestData.London(),
            TestData.OilChange,
            date,
            now,
            bays,
            technicians,
            dealershipAppointments ?? [],
            vehicleAppointments ?? [],
            ignoredAppointmentId);

        return Assert.IsType<AvailabilityResult.Slots>(result).Ranges;
    }

    [Fact]
    public void V1_EmptyDay_ListsEveryQuarterHourFromOpeningToLastStart()
    {
        var bay = TestData.NewBay("Bay 1");
        var technician = TestData.NewTechnician("Alice", TestData.OilChange);

        var ranges = Calculate(Monday, TestData.Now, [bay], [technician]);

        Assert.Equal(37, ranges.Count);
        Assert.Equal(TestData.Utc("2030-01-07T08:00:00Z"), ranges[0].Start);
        Assert.Equal(TestData.Utc("2030-01-07T17:00:00Z"), ranges[^1].Start);
        Assert.Equal(TestData.Utc("2030-01-07T18:00:00Z"), ranges[^1].End);
    }

    [Fact]
    public void V2_AllGeneralBaysBusy_OmitsEveryStartOverlappingTheBusyPeriod()
    {
        var bay1 = TestData.NewBay("Bay 1");
        var bay2 = TestData.NewBay("Bay 2");
        var alice = TestData.NewTechnician("Alice", TestData.OilChange);
        var bob = TestData.NewTechnician("Bob", TestData.OilChange);
        var busy = new[]
        {
            TestData.Booked(bay1, alice, "2030-01-07T09:00:00Z"),
            TestData.Booked(bay2, bob, "2030-01-07T09:00:00Z"),
        };

        var starts = Calculate(Monday, TestData.Now, [bay1, bay2], [alice, bob], busy)
            .Select(range => range.Start)
            .ToList();

        for (var start = TestData.Utc("2030-01-07T08:15:00Z");
             start <= TestData.Utc("2030-01-07T09:45:00Z");
             start += TimeSpan.FromMinutes(15))
        {
            Assert.DoesNotContain(start, starts);
        }

        Assert.Contains(TestData.Utc("2030-01-07T08:00:00Z"), starts);
        Assert.Contains(TestData.Utc("2030-01-07T10:00:00Z"), starts);
    }

    [Fact]
    public void V4_NowInsideTheDay_ListsOnlyStartsAfterNow()
    {
        var bay = TestData.NewBay("Bay 1");
        var technician = TestData.NewTechnician("Alice", TestData.OilChange);

        var ranges = Calculate(Monday, TestData.Utc("2030-01-07T10:05:00Z"), [bay], [technician]);

        Assert.Equal(TestData.Utc("2030-01-07T10:15:00Z"), ranges[0].Start);
    }

    [Fact]
    public void V5_PastDateOrClosedDay_ListsNothing()
    {
        var bay = TestData.NewBay("Bay 1");
        var technician = TestData.NewTechnician("Alice", TestData.OilChange);

        var pastDay = Calculate(Monday, TestData.Utc("2030-01-08T00:00:00Z"), [bay], [technician]);
        var sunday = Calculate(new DateOnly(2030, 1, 6), TestData.Now, [bay], [technician]);

        Assert.Empty(pastDay);
        Assert.Empty(sunday);
    }

    [Fact]
    public void V6_SummerDate_ConvertsLocalOpeningTimeToUtc()
    {
        var bay = TestData.NewBay("Bay 1");
        var technician = TestData.NewTechnician("Alice", TestData.OilChange);

        var ranges = Calculate(new DateOnly(2030, 7, 1), TestData.Now, [bay], [technician]);

        Assert.Equal(TestData.Utc("2030-07-01T07:00:00Z"), ranges[0].Start);
    }

    [Fact]
    public void A1_IgnoredAppointment_FreesItsOwnResourcesForAShiftedStart()
    {
        var bay1 = TestData.NewBay("Bay 1");
        var bay2 = TestData.NewBay("Bay 2");
        var alice = TestData.NewTechnician("Alice", TestData.OilChange);
        var bob = TestData.NewTechnician("Bob", TestData.OilChange);
        var own = TestData.Booked(bay1, alice, "2030-01-07T09:00:00Z");
        var other = TestData.Booked(bay2, bob, "2030-01-07T09:00:00Z");
        var shifted = TestData.Utc("2030-01-07T09:30:00Z");

        var withoutIgnoring = Calculate(Monday, TestData.Now, [bay1, bay2], [alice, bob], [own, other]);
        var ignoringOwn = Calculate(
            Monday, TestData.Now, [bay1, bay2], [alice, bob], [own, other], ignoredAppointmentId: own.Id);

        Assert.DoesNotContain(shifted, withoutIgnoring.Select(range => range.Start));
        Assert.Contains(shifted, ignoringOwn.Select(range => range.Start));
    }

    [Fact]
    public void A2_VehicleHasAnotherAppointment_OmitsStartsOverlappingIt()
    {
        var bay1 = TestData.NewBay("Bay 1");
        var bay2 = TestData.NewBay("Bay 2");
        var alice = TestData.NewTechnician("Alice", TestData.OilChange);
        var vehicleId = Guid.NewGuid();
        var vehicleAppointment = TestData.Booked(bay2, alice, "2030-01-07T14:00:00Z", vehicleId: vehicleId);

        var starts = Calculate(
                Monday, TestData.Now, [bay1], [alice], vehicleAppointments: [vehicleAppointment])
            .Select(range => range.Start)
            .ToList();

        for (var start = TestData.Utc("2030-01-07T13:15:00Z");
             start <= TestData.Utc("2030-01-07T14:45:00Z");
             start += TimeSpan.FromMinutes(15))
        {
            Assert.DoesNotContain(start, starts);
        }

        Assert.Contains(TestData.Utc("2030-01-07T13:00:00Z"), starts);
        Assert.Contains(TestData.Utc("2030-01-07T15:00:00Z"), starts);
    }
}
