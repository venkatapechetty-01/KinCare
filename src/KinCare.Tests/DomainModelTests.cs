using FluentAssertions;
using KinCare.API.Domain;

namespace KinCare.Tests;

public class DomainModelTests
{
    [Fact]
    public void Organization_Defaults_StarterPlan()
    {
        var org = new Organization();
        org.PlanTier.Should().Be(PlanTier.Starter);
    }

    [Fact]
    public void Organization_Defaults_IsActive()
    {
        var org = new Organization();
        org.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Resident_Defaults_IsActive()
    {
        var resident = new Resident();
        resident.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Resident_Defaults_NoSpecialNeeds()
    {
        var resident = new Resident();
        resident.NeedsWheelchair.Should().BeFalse();
        resident.NeedsOxygen.Should().BeFalse();
        resident.NeedsStretcher.Should().BeFalse();
        resident.NeedsWalker.Should().BeFalse();
    }

    [Fact]
    public void Vendor_Defaults_IsActive()
    {
        var vendor = new Vendor();
        vendor.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Vendor_Defaults_BasicCapabilityTier()
    {
        var vendor = new Vendor();
        vendor.CapabilityTier.Should().Be(VendorCapabilityTier.Basic);
    }

    [Fact]
    public void Ride_Defaults_DispatchedStatus()
    {
        var ride = new Ride();
        ride.Status.Should().Be(RideStatus.Dispatched);
    }

    [Fact]
    public void Facility_Defaults_IsActive()
    {
        var facility = new Facility();
        facility.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Facility_Defaults_EasternTimezone()
    {
        var facility = new Facility();
        facility.Timezone.Should().Be("America/New_York");
    }

    [Fact]
    public void PlanTier_Ordering_StarterLessThanProfessional()
    {
        (PlanTier.Starter < PlanTier.Professional).Should().BeTrue();
    }

    [Fact]
    public void PlanTier_Ordering_ProfessionalLessThanEnterprise()
    {
        (PlanTier.Professional < PlanTier.Enterprise).Should().BeTrue();
    }

    [Fact]
    public void RideStatus_HasExpectedValues()
    {
        var values = Enum.GetValues<RideStatus>();
        values.Should().HaveCount(12);
        values.Should().Contain(RideStatus.Dispatched);
        values.Should().Contain(RideStatus.Confirmed);
        values.Should().Contain(RideStatus.EnRoute);
        values.Should().Contain(RideStatus.Arrived);
        values.Should().Contain(RideStatus.PickedUp);
        values.Should().Contain(RideStatus.AtDestination);
        values.Should().Contain(RideStatus.Dropped);
        values.Should().Contain(RideStatus.AwaitingReturn);
        values.Should().Contain(RideStatus.ReturnEnRoute);
        values.Should().Contain(RideStatus.ReturnPickedUp);
        values.Should().Contain(RideStatus.Completed);
        values.Should().Contain(RideStatus.Cancelled);
    }

    [Fact]
    public void DispatchChannel_HasExpectedValues()
    {
        var values = Enum.GetValues<DispatchChannel>();
        values.Should().Contain(DispatchChannel.SmsTaxi);
        values.Should().Contain(DispatchChannel.SmsNemt);
        values.Should().Contain(DispatchChannel.Broker);
    }
}
