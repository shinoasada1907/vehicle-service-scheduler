using Scheduler.Domain.Resources;
using Scheduler.Domain.Scheduling;

namespace Scheduler.UnitTests.Scheduling;

public sealed class ResourceAllocatorTests
{
    private static readonly TimeRange NineToTen =
        TimeRange.FromDuration(TestData.Utc("2030-01-07T09:00:00Z"), TimeSpan.FromHours(1));

    [Fact]
    public void S5_TechnicianBookedPartlyInsideRequestedPeriod_AllocatesOtherTechnician()
    {
        var bay = TestData.NewBay("Bay 1");
        var otherBay = TestData.NewBay("Bay 2");
        var technician2 = TestData.NewTechnician("T2", TestData.OilChange);
        var technician3 = TestData.NewTechnician("T3", TestData.OilChange);
        var booking = TestData.Booked(otherBay, technician2, "2030-01-07T09:30:00Z");

        var result = ResourceAllocator.Allocate(
            TestData.OilChange, NineToTen, [bay], [technician2, technician3], [booking]);

        var allocated = Assert.IsType<AllocationResult.Allocated>(result);
        Assert.Same(technician3, allocated.Technician);
    }

    [Fact]
    public void S6_TechnicianBookedPartlyBeforeRequestedPeriod_AllocatesOtherTechnician()
    {
        var bay = TestData.NewBay("Bay 1");
        var otherBay = TestData.NewBay("Bay 2");
        var technician2 = TestData.NewTechnician("T2", TestData.OilChange);
        var technician3 = TestData.NewTechnician("T3", TestData.OilChange);
        var booking = TestData.Booked(otherBay, technician2, "2030-01-07T08:30:00Z");

        var result = ResourceAllocator.Allocate(
            TestData.OilChange, NineToTen, [bay], [technician2, technician3], [booking]);

        var allocated = Assert.IsType<AllocationResult.Allocated>(result);
        Assert.Same(technician3, allocated.Technician);
    }

    [Fact]
    public void S7_BayBookedPartlyInsideRequestedPeriod_AllocatesOtherBay()
    {
        var bay1 = TestData.NewBay("Bay 1");
        var bay2 = TestData.NewBay("Bay 2");
        var technician = TestData.NewTechnician("Alice", TestData.OilChange);
        var otherTechnician = TestData.NewTechnician("Bob", TestData.OilChange);
        var booking = TestData.Booked(bay1, otherTechnician, "2030-01-07T09:30:00Z");

        var result = ResourceAllocator.Allocate(
            TestData.OilChange, NineToTen, [bay1, bay2], [technician], [booking]);

        var allocated = Assert.IsType<AllocationResult.Allocated>(result);
        Assert.Same(bay2, allocated.Bay);
    }

    [Fact]
    public void S8_BookingEndsWhenRequestedPeriodStarts_AllocatesSameResources()
    {
        var bay1 = TestData.NewBay("Bay 1");
        var alice = TestData.NewTechnician("Alice", TestData.OilChange);
        var booking = TestData.Booked(bay1, alice, "2030-01-07T09:00:00Z");
        var tenToEleven = TimeRange.FromDuration(TestData.Utc("2030-01-07T10:00:00Z"), TimeSpan.FromHours(1));

        var result = ResourceAllocator.Allocate(
            TestData.OilChange, tenToEleven, [bay1], [alice], [booking]);

        var allocated = Assert.IsType<AllocationResult.Allocated>(result);
        Assert.Same(bay1, allocated.Bay);
        Assert.Same(alice, allocated.Technician);
    }

    [Fact]
    public void S9_ServiceNeedsGeneralBay_SkipsIncompatibleBayAndUnqualifiedTechnician()
    {
        var electricVehicleBay = TestData.NewBay("A EV bay", BayType.ElectricVehicle);
        var generalBay = TestData.NewBay("Z general bay");
        var aaron = TestData.NewTechnician("Aaron", TestData.WheelAlignment);
        var zoe = TestData.NewTechnician("Zoe", TestData.OilChange);

        var result = ResourceAllocator.Allocate(
            TestData.OilChange, NineToTen, [electricVehicleBay, generalBay], [aaron, zoe], []);

        var allocated = Assert.IsType<AllocationResult.Allocated>(result);
        Assert.Same(generalBay, allocated.Bay);
        Assert.Same(zoe, allocated.Technician);
    }

    [Fact]
    public void S10_SeveralFreeCandidates_PicksFirstByNameWhateverTheInputOrder()
    {
        var bayA = TestData.NewBay("Bay A");
        var bayB = TestData.NewBay("Bay B");
        var alice = TestData.NewTechnician("Alice", TestData.OilChange);
        var ben = TestData.NewTechnician("Ben", TestData.OilChange);

        var first = ResourceAllocator.Allocate(
            TestData.OilChange, NineToTen, [bayB, bayA], [ben, alice], []);
        var second = ResourceAllocator.Allocate(
            TestData.OilChange, NineToTen, [bayA, bayB], [alice, ben], []);

        foreach (var result in new[] { first, second })
        {
            var allocated = Assert.IsType<AllocationResult.Allocated>(result);
            Assert.Same(bayA, allocated.Bay);
            Assert.Same(alice, allocated.Technician);
        }
    }

    [Fact]
    public void S11_AllCompatibleBaysBooked_ReturnsNoCapacityForBays()
    {
        var bay = TestData.NewBay("Bay 1");
        var freeTechnician = TestData.NewTechnician("Alice", TestData.OilChange);
        var otherTechnician = TestData.NewTechnician("Bob", TestData.OilChange);
        var booking = TestData.Booked(bay, otherTechnician, "2030-01-07T09:00:00Z");

        var result = ResourceAllocator.Allocate(
            TestData.OilChange, NineToTen, [bay], [freeTechnician], [booking]);

        var noCapacity = Assert.IsType<AllocationResult.NoCapacity>(result);
        Assert.Equal(CapacityShortage.Bays, noCapacity.Shortage);
    }

    [Fact]
    public void S12_AllQualifiedTechniciansBooked_ReturnsNoCapacityForTechnicians()
    {
        var freeBay1 = TestData.NewBay("Bay 1");
        var freeBay2 = TestData.NewBay("Bay 2");
        var otherBay = TestData.NewBay("Bay 3");
        var technician = TestData.NewTechnician("Alice", TestData.OilChange);
        var booking = TestData.Booked(otherBay, technician, "2030-01-07T09:00:00Z");

        var result = ResourceAllocator.Allocate(
            TestData.OilChange, NineToTen, [freeBay1, freeBay2], [technician], [booking]);

        var noCapacity = Assert.IsType<AllocationResult.NoCapacity>(result);
        Assert.Equal(CapacityShortage.Technicians, noCapacity.Shortage);
    }

    [Fact]
    public void S13_BayAndTechnicianBothBooked_ReturnsNoCapacityForBaysAndTechnicians()
    {
        var bay = TestData.NewBay("Bay 1");
        var technician = TestData.NewTechnician("Alice", TestData.OilChange);
        var booking = TestData.Booked(bay, technician, "2030-01-07T09:00:00Z");

        var result = ResourceAllocator.Allocate(
            TestData.OilChange, NineToTen, [bay], [technician], [booking]);

        var noCapacity = Assert.IsType<AllocationResult.NoCapacity>(result);
        Assert.Equal(CapacityShortage.BaysAndTechnicians, noCapacity.Shortage);
    }

    [Fact]
    public void S14_NoBayOfRequiredType_ReturnsServiceNotOffered()
    {
        var generalBay = TestData.NewBay("Bay 1");
        var technician = TestData.NewTechnician("Alice", TestData.WheelAlignment);

        var result = ResourceAllocator.Allocate(
            TestData.WheelAlignment, NineToTen, [generalBay], [technician], []);

        Assert.IsType<AllocationResult.ServiceNotOffered>(result);
    }

    [Fact]
    public void S15_NoQualifiedTechnician_ReturnsServiceNotOffered()
    {
        var generalBay = TestData.NewBay("Bay 1");
        var technician = TestData.NewTechnician("Alice", TestData.WheelAlignment);

        var result = ResourceAllocator.Allocate(
            TestData.OilChange, NineToTen, [generalBay], [technician], []);

        Assert.IsType<AllocationResult.ServiceNotOffered>(result);
    }
}
