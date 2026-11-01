using Scheduler.Domain.Scheduling;

namespace Scheduler.UnitTests.Scheduling;

public sealed class BookingPolicyTests
{
    [Fact]
    public void S16_VehicleOwnedByAnotherCustomer_ReturnsVehicleNotOwnedByCustomer()
    {
        var otherCustomerId = Guid.Parse("c0000000-0000-4000-8000-000000000002");

        var violation = Check("2030-01-07T09:00:00Z", bookedBy: otherCustomerId);

        Assert.Equal(BookingRuleViolation.VehicleNotOwnedByCustomer, violation);
    }

    [Theory]
    [InlineData("2030-01-07T09:00:00Z")]
    [InlineData("2030-01-07T09:15:00Z")]
    public void S17_StartNotAfterNow_ReturnsStartInPast(string nowUtc)
    {
        var violation = Check("2030-01-07T09:00:00Z", now: TestData.Utc(nowUtc));

        Assert.Equal(BookingRuleViolation.StartInPast, violation);
    }

    [Fact]
    public void S18_StartOffSlotBoundary_ReturnsStartNotOnSlotBoundary()
    {
        var violation = Check("2030-01-07T09:10:00Z");

        Assert.Equal(BookingRuleViolation.StartNotOnSlotBoundary, violation);
    }

    [Theory]
    [InlineData("2030-01-06T10:00:00Z")]
    [InlineData("2030-01-07T07:45:00Z")]
    [InlineData("2030-01-07T17:30:00Z")]
    public void S19_PeriodOutsideOpeningHours_ReturnsOutsideOpeningHours(string startUtc)
    {
        var violation = Check(startUtc);

        Assert.Equal(BookingRuleViolation.OutsideOpeningHours, violation);
    }

    [Theory]
    [InlineData("2030-01-07T08:00:00Z")]
    [InlineData("2030-07-01T07:00:00Z")]
    public void S20_StartAtLocalOpeningTime_ReturnsNull(string startUtc)
    {
        var violation = Check(startUtc);

        Assert.Null(violation);
    }

    [Fact]
    public void S20_SummerStartBeforeLocalOpening_ReturnsOutsideOpeningHours()
    {
        var violation = Check("2030-07-01T06:45:00Z");

        Assert.Equal(BookingRuleViolation.OutsideOpeningHours, violation);
    }

    [Fact]
    public void S21_PeriodEndsExactlyAtClosing_ReturnsNull()
    {
        var violation = Check("2030-01-07T17:00:00Z");

        Assert.Null(violation);
    }

    private static BookingRuleViolation? Check(string startUtc, Guid? bookedBy = null, DateTimeOffset? now = null)
    {
        var period = TimeRange.FromDuration(TestData.Utc(startUtc), TimeSpan.FromHours(1));

        return BookingPolicy.Check(
            TestData.London(),
            TestData.NewVehicle(TestData.CustomerId),
            bookedBy ?? TestData.CustomerId,
            period,
            now ?? TestData.Now);
    }
}
