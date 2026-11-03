using Scheduler.Domain.Appointments;
using Scheduler.Domain.Scheduling;

namespace Scheduler.UnitTests.Scheduling;

public sealed class ChangePolicyTests
{
    private static readonly DateTimeOffset Now = TestData.Utc("2030-01-07T09:00:00Z");

    private static readonly TimeSpan TwoHours = TimeSpan.FromHours(2);

    private static Appointment StartingAt(string startUtc) =>
        TestData.Booked(TestData.NewBay("Bay 1"), TestData.NewTechnician("Alice", TestData.OilChange), startUtc);

    [Fact]
    public void R6_StartsBeyondTheCutoff_IsAllowed()
    {
        var appointment = StartingAt("2030-01-07T11:15:00Z");

        Assert.Null(ChangePolicy.Check(appointment, Now, TwoHours));
    }

    [Fact]
    public void C4_StartsExactlyAtTheCutoff_WindowIsClosed()
    {
        var appointment = StartingAt("2030-01-07T11:00:00Z");

        Assert.Equal(ChangeRuleViolation.ChangeWindowClosed, ChangePolicy.Check(appointment, Now, TwoHours));
    }

    [Fact]
    public void C4_AlreadyStarted_WindowIsClosed()
    {
        var appointment = StartingAt("2030-01-07T08:30:00Z");

        Assert.Equal(ChangeRuleViolation.ChangeWindowClosed, ChangePolicy.Check(appointment, Now, TwoHours));
    }

    [Fact]
    public void R7_CancelledAppointment_ReportsAppointmentCancelled()
    {
        var appointment = StartingAt("2030-01-07T15:00:00Z");
        appointment.Cancel(Now);

        Assert.Equal(ChangeRuleViolation.AppointmentCancelled, ChangePolicy.Check(appointment, Now, TwoHours));
    }

    [Fact]
    public void X1_ShorterCutoff_AllowsAStartBeyondIt()
    {
        var appointment = StartingAt("2030-01-07T10:15:00Z");

        Assert.Null(ChangePolicy.Check(appointment, Now, TimeSpan.FromHours(1)));
    }

    [Fact]
    public void C1_Cancel_SetsStatusAndCancelledAt()
    {
        var appointment = StartingAt("2030-01-07T15:00:00Z");

        appointment.Cancel(Now);

        Assert.Equal(AppointmentStatus.Cancelled, appointment.Status);
        Assert.Equal(Now, appointment.CancelledAt);
    }
}
