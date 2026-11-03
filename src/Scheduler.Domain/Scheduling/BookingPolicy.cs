using Scheduler.Domain.Customers;
using Scheduler.Domain.Dealerships;

namespace Scheduler.Domain.Scheduling;

public static class BookingPolicy
{
    public static readonly TimeSpan SlotLength = TimeSpan.FromMinutes(15);

    public static BookingRuleViolation? Check(
        Dealership dealership,
        Vehicle vehicle,
        Guid customerId,
        TimeRange period,
        DateTimeOffset now)
    {
        if (vehicle.CustomerId != customerId)
        {
            return BookingRuleViolation.VehicleNotOwnedByCustomer;
        }

        return CheckTiming(dealership, period, now);
    }

    public static BookingRuleViolation? CheckTiming(Dealership dealership, TimeRange period, DateTimeOffset now)
    {
        if (period.Start <= now)
        {
            return BookingRuleViolation.StartInPast;
        }

        if (dealership.ToLocalTime(period.Start).TimeOfDay.Ticks % SlotLength.Ticks != 0)
        {
            return BookingRuleViolation.StartNotOnSlotBoundary;
        }

        if (!dealership.IsOpenThroughout(period))
        {
            return BookingRuleViolation.OutsideOpeningHours;
        }

        return null;
    }
}
