namespace Scheduler.Domain.Scheduling;

public enum BookingRuleViolation
{
    VehicleNotOwnedByCustomer,
    StartInPast,
    StartNotOnSlotBoundary,
    OutsideOpeningHours,
}
