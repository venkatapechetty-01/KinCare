using FluentAssertions;
using KinCare.API.Data;
using KinCare.API.Domain;
using KinCare.API.Services;
using KinCare.API.Services.Dispatch;
using Microsoft.EntityFrameworkCore;

namespace KinCare.Tests;

public class DispatchRouterTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly DispatchRouter _sut;
    private readonly Guid _facilityId = Guid.NewGuid();
    private readonly Guid _orgId = Guid.NewGuid();

    public DispatchRouterTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("DispatchRouterTests_" + Guid.NewGuid())
            .Options;
        _db = new AppDbContext(options);
        _sut = new DispatchRouter(_db, new PlanGate());
    }

    [Fact]
    public async Task RouteAsync_WheelchairResident_WithNemtVendor_RoutesToSmsNemt()
    {
        var vendor = SeedVendor(DispatchMethod.SmsNemt);
        var resident = new Resident { NeedsWheelchair = true, FacilityId = _facilityId };
        var org = new Organization { Id = _orgId, PlanTier = PlanTier.Starter };
        var facility = new Facility { Id = _facilityId };

        var (channel, vendors) = await _sut.RouteAsync(resident, org, facility);

        channel.Should().Be(DispatchChannel.SmsNemt);
        vendors.Should().ContainSingle(v => v.Id == vendor.Id);
    }

    [Fact]
    public async Task RouteAsync_WheelchairResident_NoNemtVendor_BrokerEnabled_RoutesToBroker()
    {
        var resident = new Resident { NeedsWheelchair = true, FacilityId = _facilityId };
        var org = new Organization { Id = _orgId, PlanTier = PlanTier.Professional, BrokerEnabled = true };
        var facility = new Facility { Id = _facilityId };

        var (channel, vendors) = await _sut.RouteAsync(resident, org, facility);

        channel.Should().Be(DispatchChannel.Broker);
        vendors.Should().BeEmpty();
    }

    [Fact]
    public async Task RouteAsync_WheelchairResident_NoVendor_BrokerDisabled_FallsBackToSmsNemt()
    {
        var resident = new Resident { NeedsWheelchair = true, FacilityId = _facilityId };
        var org = new Organization { Id = _orgId, PlanTier = PlanTier.Starter, BrokerEnabled = false };
        var facility = new Facility { Id = _facilityId };

        var (channel, vendors) = await _sut.RouteAsync(resident, org, facility);

        channel.Should().Be(DispatchChannel.SmsNemt);
        vendors.Should().BeEmpty();
    }

    [Fact]
    public async Task RouteAsync_OxygenResident_TreatedAsSpecialTransport()
    {
        SeedVendor(DispatchMethod.SmsNemt);
        var resident = new Resident { NeedsOxygen = true, FacilityId = _facilityId };
        var org = new Organization { Id = _orgId, PlanTier = PlanTier.Starter };
        var facility = new Facility { Id = _facilityId };

        var (channel, _) = await _sut.RouteAsync(resident, org, facility);

        channel.Should().Be(DispatchChannel.SmsNemt);
    }

    [Fact]
    public async Task RouteAsync_StretcherResident_TreatedAsSpecialTransport()
    {
        SeedVendor(DispatchMethod.SmsNemt);
        var resident = new Resident { NeedsStretcher = true, FacilityId = _facilityId };
        var org = new Organization { Id = _orgId, PlanTier = PlanTier.Starter };
        var facility = new Facility { Id = _facilityId };

        var (channel, _) = await _sut.RouteAsync(resident, org, facility);

        channel.Should().Be(DispatchChannel.SmsNemt);
    }

    [Fact]
    public async Task RouteAsync_StandardResident_WithTaxiVendor_RoutesToSmsTaxi()
    {
        SeedVendor(DispatchMethod.SmsTaxi);
        var resident = new Resident { FacilityId = _facilityId };
        var org = new Organization { Id = _orgId, PlanTier = PlanTier.Starter };
        var facility = new Facility { Id = _facilityId };

        var (channel, vendors) = await _sut.RouteAsync(resident, org, facility);

        channel.Should().Be(DispatchChannel.SmsTaxi);
        vendors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task RouteAsync_StandardResident_NoUber_WithTaxi_RoutesToSmsTaxi()
    {
        SeedVendor(DispatchMethod.SmsTaxi);
        var resident = new Resident { FacilityId = _facilityId };
        var org = new Organization { Id = _orgId, PlanTier = PlanTier.Starter };
        var facility = new Facility { Id = _facilityId };

        var (channel, vendors) = await _sut.RouteAsync(resident, org, facility);

        channel.Should().Be(DispatchChannel.SmsTaxi);
        vendors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task RouteAsync_StandardResident_NoTaxi_NoUber_WithNemt_RoutesToSmsNemt()
    {
        SeedVendor(DispatchMethod.SmsNemt);
        var resident = new Resident { FacilityId = _facilityId };
        var org = new Organization { Id = _orgId, PlanTier = PlanTier.Starter };
        var facility = new Facility { Id = _facilityId };

        var (channel, vendors) = await _sut.RouteAsync(resident, org, facility);

        channel.Should().Be(DispatchChannel.SmsNemt);
        vendors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task RouteAsync_StandardResident_NoVendors_BrokerEnabled_RoutesToBroker()
    {
        var resident = new Resident { FacilityId = _facilityId };
        var org = new Organization { Id = _orgId, PlanTier = PlanTier.Professional, BrokerEnabled = true };
        var facility = new Facility { Id = _facilityId };

        var (channel, vendors) = await _sut.RouteAsync(resident, org, facility);

        channel.Should().Be(DispatchChannel.Broker);
        vendors.Should().BeEmpty();
    }

    [Fact]
    public async Task RouteAsync_StandardResident_NoVendors_BrokerDisabled_FallsBackToSmsNemt()
    {
        var resident = new Resident { FacilityId = _facilityId };
        var org = new Organization { Id = _orgId, PlanTier = PlanTier.Starter, BrokerEnabled = false };
        var facility = new Facility { Id = _facilityId };

        var (channel, vendors) = await _sut.RouteAsync(resident, org, facility);

        channel.Should().Be(DispatchChannel.SmsNemt);
        vendors.Should().BeEmpty();
    }

    [Fact]
    public async Task RouteAsync_InactiveVendor_IsIgnored()
    {
        var vendor = new Vendor
        {
            Id = Guid.NewGuid(),
            FacilityId = _facilityId,
            Name = "Inactive",
            PhoneNumber = "555-0000",
            DispatchMethod = DispatchMethod.SmsTaxi,
            IsActive = false
        };
        _db.Vendors.Add(vendor);
        await _db.SaveChangesAsync();

        var resident = new Resident { FacilityId = _facilityId };
        var org = new Organization { Id = _orgId, PlanTier = PlanTier.Starter };
        var facility = new Facility { Id = _facilityId };

        var (channel, vendors) = await _sut.RouteAsync(resident, org, facility);

        channel.Should().Be(DispatchChannel.SmsNemt);
        vendors.Should().BeEmpty();
    }

    [Fact]
    public async Task RouteAsync_MultipleNemtVendors_ReturnsAllActive()
    {
        SeedVendor(DispatchMethod.SmsNemt);
        SeedVendor(DispatchMethod.SmsNemt);
        var resident = new Resident { NeedsWheelchair = true, FacilityId = _facilityId };
        var org = new Organization { Id = _orgId, PlanTier = PlanTier.Starter };
        var facility = new Facility { Id = _facilityId };

        var (channel, vendors) = await _sut.RouteAsync(resident, org, facility);

        channel.Should().Be(DispatchChannel.SmsNemt);
        vendors.Should().HaveCount(2);
    }

    private Vendor SeedVendor(DispatchMethod method)
    {
        var vendor = new Vendor
        {
            Id = Guid.NewGuid(),
            FacilityId = _facilityId,
            Name = $"Vendor_{method}_{Guid.NewGuid():N}",
            PhoneNumber = $"555-{Guid.NewGuid().ToString()[..4]}",
            DispatchMethod = method,
            IsActive = true
        };
        _db.Vendors.Add(vendor);
        _db.SaveChanges();
        return vendor;
    }

    public void Dispose() => _db.Dispose();
}
